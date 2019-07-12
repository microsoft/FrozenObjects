#!/bin/sh

clang++ -shared -o $1/Microsoft.FrozenObjects.Serializer.Native.so --no-undefined -Wno-invalid-noreturn -fPIC -std=c++11 -O2 Serializer/native/Microsoft.FrozenObjects.Serializer.Native.cpp
