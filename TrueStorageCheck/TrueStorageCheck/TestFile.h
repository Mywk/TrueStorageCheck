/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
#pragma once

#include <string>
#include <vector>

class TestFile
{
public:
    TestFile(const std::string& path, unsigned long long totalSize);

    std::string Path;
    unsigned long long BytesWritten;

    unsigned long long TotalSize;

    /// <summary>
    /// We save part of the generated data to quickly verify later if necessary, Data is usually DataBlockSize
    /// </summary>
    std::vector<unsigned char> Data;
    unsigned long long DataSize;

    /// <summary>
    /// Setters
    /// </summary>
    void SetBytesWritten(unsigned long long bytesWritten);
    void SetData(const unsigned char* pData, unsigned long long dataSize);

private:
    
};

