#pragma once

#include <cstdint>
#include <cstddef>

#define near

#ifdef WINDOWS

#else
typedef unsigned short WORD;
typedef unsigned int DWORD;
typedef int BOOL;
typedef unsigned char BYTE;
typedef BYTE near *PBYTE;
typedef uintptr_t ObjectID;
typedef int HRESULT;
#endif

#define LIMITED_METHOD_CONTRACT
#define LIMITED_METHOD_DAC_CONTRACT
#define FEATURE_COMINTEROP
#define FEATURE_COLLECTIBLE_TYPES

#if INTPTR_MAX == INT64_MAX
#define OBJHEADER_SIZE (sizeof(DWORD) /* m_alignpad */ + sizeof(DWORD) /* m_SyncBlockValue */)
#else
#define OBJHEADER_SIZE sizeof(DWORD) /* m_SyncBlockValue */
#endif

#if INTPTR_MAX == INT64_MAX
#define TARGET_POINTER_SIZE 8
#else
#define TARGET_POINTER_SIZE 4
#endif

#define MIN_OBJECT_SIZE (2 * TARGET_POINTER_SIZE + OBJHEADER_SIZE)

#define DATA_ALIGNMENT 8

#define PTRALIGNCONST (DATA_ALIGNMENT - 1)

#ifndef PtrAlign
#define PtrAlign(size) \
    ((size + PTRALIGNCONST) & (~PTRALIGNCONST))
#endif //!PtrAlign