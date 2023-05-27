/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
#include "TestFile.hpp"

TestFile::TestFile(const std::string& path, unsigned long long totalSize) : Path(path), TotalSize(totalSize) {
    BytesWritten  = DataSize = 0;
}

/// <summary>
/// Setter for BytesWritten
/// </summary>
/// <param name="bytesWritten"></param>
void TestFile::SetBytesWritten(unsigned long long bytesWritten) {
    BytesWritten = bytesWritten;
}

/// <summary>
/// Setter for Data
/// </summary>
/// <param name="data">Pointer to the data to copy to this TestFile</param>
/// <param name="dataSize">Size of the data in our pointer</param>
void TestFile::SetData(const unsigned char* pData, unsigned long long dataSize) {

    DataSize = dataSize;

    // Resize our Data vector and copy data
    Data.resize(dataSize); 
    memcpy(&Data[0], pData, dataSize);

}

