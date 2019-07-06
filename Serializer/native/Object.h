#pragma once

#include "Types.h"
#include "MethodTable.h"

class Object
{
protected:
    MethodTable *pMT;

public:
    inline size_t GetSize();
    inline DWORD GetNumComponents();
    inline MethodTable *RawGetMethodTable();
};

class ArrayBase : public Object
{
    friend class Object;

private:
    DWORD m_NumComponents;
};

inline DWORD Object::GetNumComponents()
{
    return ((ArrayBase *)this)->m_NumComponents;
}

inline size_t Object::GetSize()
{
    size_t s = pMT->GetBaseSize();
    if (pMT->HasComponentSize())
    {
        s += (size_t)GetNumComponents() * pMT->RawGetComponentSize();
    }
    return s;
}

inline MethodTable *Object::RawGetMethodTable()
{
    return pMT;
}