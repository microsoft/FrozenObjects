#pragma once

#include "Types.h"

typedef BOOL (*WalkObjectFunc)(ObjectID, ObjectID *, void *);

static int ComputeSize(int series)
{
    return sizeof(size_t) + series * sizeof(size_t) * 2;
}

class GCDesc
{
private:
    uint8_t *data;
    size_t size;

    int32_t GetNumSeries()
    {
#if INTPTR_MAX == INT64_MAX
        return (int32_t)(*(int64_t *)(this->data + this->size - sizeof(size_t)));
#else
        return (int32_t)(*(int32_t *)(this->data + this->size - sizeof(size_t)));
#endif
    }

    int32_t GetHighestSeries()
    {
        return (int32_t)(this->size - sizeof(size_t) * 3);
    }

    int32_t GetLowestSeries()
    {
        return (int32_t)(this->size - ComputeSize(this->GetNumSeries()));
    }

    int32_t GetSeriesSize(int curr)
    {
#if INTPTR_MAX == INT64_MAX
        return (int32_t)(*(int64_t *)(this->data + curr));
#else
        return (int32_t)(*(int32_t *)(this->data + curr));
#endif
    }

    uint64_t GetSeriesOffset(int curr)
    {
#if INTPTR_MAX == INT64_MAX
        return (uint64_t)(*(uint64_t *)(this->data + curr + sizeof(size_t)));
#else
        return (uint64_t)(*(uint32_t *)(this->data + curr + sizeof(size_t)));
#endif
    }

    uint32_t GetPointers(int curr, int i)
    {
        int32_t offset = i * sizeof(size_t);
#if INTPTR_MAX == INT64_MAX
        return (uint32_t) * (uint32_t *)(this->data + curr + offset);
#else
        return (uint32_t) * (uint16_t *)(this->data + curr + offset);
#endif
    }

    uint32_t GetSkip(int curr, int i)
    {
        int32_t offset = i * sizeof(size_t) + sizeof(size_t) / 2;
#if INTPTR_MAX == INT64_MAX
        return (uint32_t) * (uint32_t *)(this->data + curr + offset);
#else
        return (uint32_t) * (uint16_t *)(this->data + curr + offset);
#endif
    }

public:
    GCDesc(uint8_t *data, size_t size) : data(data), size(size)
    {
    }

    void WalkObject(PBYTE addr, size_t size, void *context, WalkObjectFunc refCallback)
    {
        int32_t series = this->GetNumSeries();
        int32_t highest = this->GetHighestSeries();
        int32_t curr = highest;

        if (series > 0)
        {
            int32_t lowest = this->GetLowestSeries();
            do
            {
                auto ptr = addr + this->GetSeriesOffset(curr);
                auto stop = ptr + GetSeriesSize(curr) + size;

                while (ptr < stop)
                {
                    auto ret = *(size_t *)ptr;
                    if (ret != 0)
                    {
                        refCallback((ObjectID)addr, (ObjectID *)ptr, context);
                    }

                    ptr += sizeof(size_t);
                }

                curr -= sizeof(size_t) * 2;
            } while (curr >= lowest);
        }
        else
        {
            auto ptr = addr + this->GetSeriesOffset(curr);
            while (ptr < addr + size - sizeof(size_t))
            {
                for (int32_t i = 0; i > series; i--)
                {
                    uint32_t nptrs = this->GetPointers(curr, i);
                    uint32_t skip = this->GetSkip(curr, i);

                    auto stop = ptr + (nptrs * sizeof(size_t));
                    do
                    {
                        auto ret = *(size_t *)ptr;
                        if (ret != 0)
                        {
                            refCallback((ObjectID)addr, (ObjectID *)ptr, context);
                        }

                        ptr += sizeof(size_t);
                    } while (ptr < stop);

                    ptr += skip;
                }
            }
        }
    }
};