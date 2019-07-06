#include "Object.h"
#include "GCDesc.h"

#include <iostream>
#include <fstream>
#include <unordered_map>
#include <queue>

#ifndef PADDING
#define PADDING(num, align) (0 - (num) & ((align)-1))
#endif

#ifdef _DEBUG
#include <cstdarg>

std::ofstream logfile("log.txt", std::ios::out | std::ios::trunc);

void print_log(const char* format, ...)
{
    static char s_printf_buf[1024];
    va_list args;
    va_start(args, format);
    _vsnprintf(s_printf_buf, sizeof(s_printf_buf), format, args);
    va_end(args);

    logfile.write(s_printf_buf, strlen(s_printf_buf));
}

#endif

struct MethodTableTokenTuple
{
    MethodTable *MT;
    size_t Token;

    MethodTableTokenTuple(MethodTable *mt, const size_t token)
    {
        this->MT = mt;
        this->Token = token;
    }
};

struct WalkObjectContext
{
    std::unordered_map<ObjectID, size_t> *SerializedObjectMap;
    std::queue<ObjectID> *ObjectQueue;
    std::ofstream *File;
    size_t ObjectStart;
    size_t *LastReservedObjectEnd;

    WalkObjectContext(std::unordered_map<ObjectID, size_t> *serializedObjectMap, std::queue<ObjectID> *objectQueue, std::ofstream *file, size_t objectStart, size_t *lastReservedObjectEnd)
    {
        this->SerializedObjectMap = serializedObjectMap;
        this->ObjectQueue = objectQueue;
        this->File = file;
        this->ObjectStart = objectStart;
        this->LastReservedObjectEnd = lastReservedObjectEnd;
    }
};

static size_t GetObjectSize2(const ObjectID objectId)
{
    auto o = reinterpret_cast<Object *>(objectId);
    size_t size = o->GetSize();

    if (size < MIN_OBJECT_SIZE)
    {
        size = PtrAlign(size);
    }

    return size;
}

static void WriteFileAtPosition(std::ofstream &file, const size_t position, void *lpBuffer, const DWORD nNumberOfBytesToWrite)
{
    file.seekp(position, std::ios::beg);
    file.write(static_cast<char *>(lpBuffer), nNumberOfBytesToWrite);
}

static void WriteFileAndMoveCurrentFilePointer(std::ofstream &file, size_t &currentFilePointer, void *lpBuffer, const DWORD nNumberOfBytesToWrite)
{
    file.write(static_cast<char *>(lpBuffer), nNumberOfBytesToWrite);
    currentFilePointer += nNumberOfBytesToWrite;
}

static BOOL EachObjectReference(const ObjectID curr, ObjectID *reference, void *context)
{
    ObjectID objectReference = *reference;
    auto walkObjectContext = static_cast<WalkObjectContext *>(context);

    auto serializedObjectMap = walkObjectContext->SerializedObjectMap;
    const auto iter = serializedObjectMap->find(objectReference);

    size_t objectReferenceDiskOffset;
    if (iter == serializedObjectMap->end())
    {
        objectReferenceDiskOffset = *walkObjectContext->LastReservedObjectEnd + sizeof(size_t); // + sizeof(size_t) because object references point to the MT*
        const size_t objectSize = GetObjectSize2(objectReference);
        *walkObjectContext->LastReservedObjectEnd += objectSize + PADDING(objectSize, sizeof(size_t));

        serializedObjectMap->insert(std::pair<ObjectID, size_t>(objectReference, objectReferenceDiskOffset));
        walkObjectContext->ObjectQueue->push(objectReference);
    }
    else
    {
        objectReferenceDiskOffset = iter->second;
    }

#ifdef _DEBUG
    print_log(R"(, { "From": %llu, "To": %llu, "Offset": %llu } )", walkObjectContext->ObjectStart + reinterpret_cast<size_t>(reference) - static_cast<size_t>(curr), objectReferenceDiskOffset, reinterpret_cast<size_t>(reference) - static_cast<size_t>(curr));
#endif

    // Do the fixups
    WriteFileAtPosition(*walkObjectContext->File, walkObjectContext->ObjectStart + reinterpret_cast<size_t>(reference) - static_cast<size_t>(curr), &objectReferenceDiskOffset, sizeof(size_t));

    return true;
}

static void EnumerateObjectReferences(const ObjectID curr, void *context)
{
    auto o = reinterpret_cast<Object *>(curr);
    const size_t size = GetObjectSize2(curr);
    auto methodTable = o->RawGetMethodTable();
    if (methodTable->ContainsPointersOrCollectible())
    {
        int entries = *reinterpret_cast<DWORD *>(reinterpret_cast<size_t>(methodTable) - sizeof(size_t));
        if (entries < 0)
        {
            entries = -entries;
        }

        const int slots = 1 + entries * 2;

        GCDesc gcdesc(reinterpret_cast<uint8_t *>(reinterpret_cast<size_t>(methodTable) - slots * sizeof(size_t)), slots * sizeof(size_t));
        gcdesc.WalkObject(reinterpret_cast<PBYTE>(curr), size, context, &EachObjectReference);
    }
}

static MethodTable *GetMTFromObject(ObjectID objectId)
{
    return reinterpret_cast<Object *>(objectId)->RawGetMethodTable();
}

extern "C" void SerializeObject(const ObjectID *root, const char *path, MethodTable *functionPointerMT, void **outMethodTableTokenTupleList, void **outMethodTableTokenTupleListVecPtr, size_t *outMethodTableTokenTupleListCount, void **outFunctionPointerFixupList, void **outFunctionPointerFixupListVecPtr, size_t *outFunctionPointerFixupListCount)
{
    std::unordered_map<ObjectID, size_t> serializedObjectMap;
    std::unordered_map<MethodTable *, size_t> mtTokenMap;
    std::queue<ObjectID> objectQueue;
    auto functionPointerFixupList = new std::vector<size_t>();
    auto methodTableTokenTupleList = new std::vector<MethodTableTokenTuple>();

    objectQueue.push(*root);
    size_t zero = 0, currentFilePointer = 0;

    // We need to find the end of this object being serialized so all object reference offsets are correctly mapped
    size_t lastReservedObjectEnd = GetObjectSize2(*root);

    lastReservedObjectEnd += PADDING(lastReservedObjectEnd, sizeof(size_t));

    std::ofstream file(path, std::ios::out | std::ios::trunc | std::ios::binary);

#ifdef _DEBUG
    print_log("[");
#endif

    while (!objectQueue.empty())
    {
        ObjectID objectId = objectQueue.front();

        MethodTable *mt = GetMTFromObject(objectId);

        size_t mtToken;
        {
            const auto iter = mtTokenMap.find(mt);
            if (iter == mtTokenMap.end())
            {
                mtToken = mtTokenMap.size();
                mtTokenMap.insert(std::pair<MethodTable *, size_t>(mt, mtToken));
                methodTableTokenTupleList->push_back(MethodTableTokenTuple(mt, mtToken));
            }
            else
            {
                mtToken = iter->second;
            }
        }

        // Write blank object header
        WriteFileAndMoveCurrentFilePointer(file, currentFilePointer, &zero, static_cast<DWORD>(sizeof(size_t)));

        size_t objectSize = GetObjectSize2(objectId);
        auto padding = static_cast<DWORD>(PADDING(objectSize, sizeof(size_t)));

#ifdef _DEBUG
        print_log("\r\n");
        print_log(R"({"MT": %d, "FP": %llu, "Size": %llu, "Padding": %d, "References": [ {"From": 0, "To": 0, "Offset": 0} )", mtToken, static_cast<long long>(file.tellp()), objectSize, padding);
#endif

        size_t objectStart = currentFilePointer;

        // functionPointerMT is basically a custom thing we've invented. It allows us to restore a function pointer to a method
        if (mt == functionPointerMT)
        {
            functionPointerFixupList->push_back(objectStart); // and so we capture the objectStart
        }

        // Write ClassID token
        WriteFileAndMoveCurrentFilePointer(file, currentFilePointer, &mtToken, static_cast<DWORD>(sizeof(size_t)));

        objectSize -= sizeof(size_t) + sizeof(size_t); // subtract space for object header and method table, since we already wrote it

        // Write object contents
        WriteFileAndMoveCurrentFilePointer(file, currentFilePointer, reinterpret_cast<PBYTE>(objectId) + sizeof(size_t), static_cast<DWORD>(objectSize)); // We start from + sizeof(size_t) because we wrote the MT

        // Write padding
        WriteFileAndMoveCurrentFilePointer(file, currentFilePointer, &zero, padding);

        WalkObjectContext context(&serializedObjectMap, &objectQueue, &file, objectStart, &lastReservedObjectEnd);
        EnumerateObjectReferences(objectId, &context);

#ifdef _DEBUG
        print_log("] }, ");
#endif

        file.seekp(currentFilePointer, std::ios::beg); // seek back to where the next object will be written

        objectQueue.pop();
    }

#ifdef _DEBUG
    print_log("\r\n");
    print_log(R"({"MT": -1, "FP": -1, "Size": -1, "Padding": -1, "References": [ {"From": 0, "To": 0, "Offset": 0} ] })");
    print_log("\r\n]");
#endif

    *outMethodTableTokenTupleList = methodTableTokenTupleList->data();
    *outMethodTableTokenTupleListVecPtr = methodTableTokenTupleList;
    *outMethodTableTokenTupleListCount = methodTableTokenTupleList->size();

    *outFunctionPointerFixupList = functionPointerFixupList->data();
    *outFunctionPointerFixupListVecPtr = functionPointerFixupList;
    *outFunctionPointerFixupListCount = functionPointerFixupList->size();

#ifdef _DEBUG
    logfile.close();
#endif
}

extern "C" void Cleanup(MethodTableTokenTuple *methodTableTokenTupleListVecPtr, size_t *functionPointerFixupListVecPtr)
{
    delete reinterpret_cast<std::vector<MethodTableTokenTuple> *>(methodTableTokenTupleListVecPtr);
    delete reinterpret_cast<std::vector<size_t> *>(functionPointerFixupListVecPtr);
}