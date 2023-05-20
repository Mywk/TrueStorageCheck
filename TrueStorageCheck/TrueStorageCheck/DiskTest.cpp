/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
#include "pch.h"
#include "DiskTest.h"

#include <ctime>
#include <random>
#include <fstream>
#include <sstream>
#include <algorithm>

// Used for cleaning all files on the drive
#include <filesystem>

// Used for time calculations (Avg Read/Write speeds)
#include <chrono>

// For now we will always test in this block size, we write 256MB at a time
const int DATA_WRITE_SIZE = 256 * (1024 * 1024);

// This is the maximum amount of random data we generate at a time
const int MAX_RAND_DATA_SIZE = 16 * (1024 * 1024);

#define up "This should never be the C drive."

DiskTest::DiskTest(char driveLetter, unsigned long long capacityToTest, bool stopOnFirstError, bool deleteTempFiles, bool writeLogFile, ProgressDelegate callback)
{
	if (driveLetter == 'C' || driveLetter == 'c') throw up;

	// I keep forgetting I can't just go + on C++
	Path = std::string(1, driveLetter) + ":\\";

	TestRunning = false;

	if (capacityToTest != 0)
		CapacityToTest = capacityToTest * (1024 * 1024);

	StopOnFirstError = stopOnFirstError;
	DeleteTempFiles = deleteTempFiles;
	WriteLogFile = writeLogFile;
	ProgressCallback = callback;
	CurrentState = State_Waiting;
	CurrentProgress = 0;

	AverageReadSpeed = AverageWriteSpeed = 0;
}

void DiskTest::GenerateData(std::vector<unsigned char>& data, const std::string& seed)
{
	std::hash<std::string> hasher;
	std::mt19937 generator(hasher(seed));
	std::uniform_int_distribution<int> distribution(0, 255);

	std::generate(data.begin(), data.end(), [&]() { return distribution(generator); });
}

/// <summary>
/// DANGER ZONE
/// </summary>
void DiskTest::DeleteAllFilesAndDirectories()
{
	// At least one check will be made, just in case
	if (Path[0] == 'C' || Path[0] == 'c') return;

	for (const auto& entry : std::filesystem::recursive_directory_iterator(Path))
		std::filesystem::remove_all(entry.path());
}

void DiskTest::RemoveDirectory(const std::string& path)
{
	if (std::filesystem::exists(path) && std::filesystem::is_directory(path)) {
		try {
			std::filesystem::remove_all(path);

			// Removal succeeded, now flush any cached data
			HANDLE handle = CreateFileA(path.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, NULL);
			if (handle != INVALID_HANDLE_VALUE) {
				FlushFileBuffers(handle);
				CloseHandle(handle);
			}
		}
		catch (const std::exception& ex) {}
	}
}

unsigned long DiskTest::GetFileSize(const std::string& filePath)
{
	WIN32_FILE_ATTRIBUTE_DATA fileInfo;
	if (!GetFileAttributesExA(filePath.c_str(), GetFileExInfoStandard, &fileInfo))
		return 0;

	ULARGE_INTEGER fileSize;
	fileSize.LowPart = fileInfo.nFileSizeLow;
	fileSize.HighPart = fileInfo.nFileSizeHigh;

	return static_cast<unsigned long>(fileSize.QuadPart);
}


bool DiskTest::PerformTest()
{
	// Tests are non re-usable for now
	if (TestRunning || CurrentState != State_Waiting) return false;

	TestRunning = true;

	std::string tempDirectoryPath = "TSC_Files";

	// Delete any temporary data that can eventually already exist, then flush the changes
	this->RemoveDirectory(Path + tempDirectoryPath);

	// Create the directory, this sometimes fails so we retry it
	for (size_t i = 0; i < 3; i++)
	{
		if (CreateDirectoryA((Path + tempDirectoryPath).c_str(), nullptr))
			break;
		else
			Sleep(100);
	}

	// Vector to store temp files that will be later deleted
	std::vector<std::string> tempFiles;

	unsigned long long freeSpace = 0;
	GetDiskSpace(Path, &this->MaxCapacity, &freeSpace);


	// Get the data block size for this disk
	DataBlockSize = GetDataBlockSize(Path);

	if (DataBlockSize == 0)
		return false;

	if (this->CapacityToTest == 0)
		this->CapacityToTest = freeSpace;

	int size = this->CapacityToTest < DATA_WRITE_SIZE ? this->CapacityToTest : DATA_WRITE_SIZE;

	unsigned long long totalDataWritten = 0;
	unsigned long long totalDataToWrite = this->CapacityToTest;

	unsigned long long dataLeftToWrite = this->CapacityToTest;

	if (freeSpace < totalDataToWrite)
		return false;

	if (ProgressCallback != NULL)
		ProgressCallback(this, (int)State_InProgress, CurrentProgress, MbWritten);

	bool ret = true;

	while (!IsDriveFull() && TestRunning && (totalDataWritten < totalDataToWrite))
	{
		if (dataLeftToWrite < size)
			size = dataLeftToWrite;

		std::string fileName = GenerateTestFileName();
		std::string filePath = Path + tempDirectoryPath + "\\" + fileName;

		// Write the test file
		auto dataWritten = WriteAndVerifyTestFile(filePath, size, StopOnFirstError);

		if (dataWritten < size)
		{
			LastSuccessfulVerifyPosition = dataWritten;
			ret = false;
		}
		else
		{
			// Store the temp file path - No longer used
			tempFiles.push_back(filePath);
		}

		// Verify the test file
		if (ret && !VerifyTestFile(filePath))
			ret = false;

		if (!ret)
			break;

		totalDataWritten += size;
		dataLeftToWrite -= size;

		MbWritten = totalDataWritten / (1024 * 1024);
		CurrentProgress = (totalDataWritten * 100) / totalDataToWrite;

		LastSuccessfulVerifyPosition = totalDataWritten;

		if (ProgressCallback != NULL)
		{
			ProgressCallback(this, (int)State_InProgress, CurrentProgress, MbWritten);
		}

	}

	// Perform final verification
	if (ret)
	{
		CurrentState = State_Verification;
		ProgressCallback(this, (int)State_Verification, CurrentProgress, MbWritten);

		for (const auto& filePath : tempFiles)
		{
			if (!VerifyTestFile(filePath))
			{
				ret = false;
				break;
			}
		}
	}

	// Delete all the temporary files
	if (DeleteTempFiles)
	{
		// No longer used
		/*for (const auto& filePath : tempFiles)
		{
			if (!DeleteTestFile(filePath))
				break;
		}*/

		// Delete any temporary data that can eventually already exist, then flush the changes
		this->RemoveDirectory(Path + tempDirectoryPath);
	}

	if (CurrentState != State_Aborted)
		CurrentState = ret ? State_Success : State_Error;

	if (ProgressCallback != NULL)
		ProgressCallback(this, CurrentState, CurrentProgress, MbWritten);

	TestRunning = false;

	return ret;
}

bool DiskTest::VerifyTestFile(const std::string& filePath)
{
	// FILE_FLAG_NO_BUFFERING is important
	HANDLE hFile = ::CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_NO_BUFFERING, NULL);

	if (hFile == INVALID_HANDLE_VALUE)
		return false;

	LARGE_INTEGER fileSize;
	if (!::GetFileSizeEx(hFile, &fileSize))
	{
		::CloseHandle(hFile);
		return false;
	}

	unsigned long long totalBytesToRead = fileSize.QuadPart;
	unsigned long long originalTotalBytesToRead = totalBytesToRead;
	unsigned long offset = 0;
	int segment = 0;

	while (totalBytesToRead > 0)
	{
		DWORD chunkSize = (DWORD)std::min<unsigned long long>(totalBytesToRead, MAX_RAND_DATA_SIZE);

		// Re-generate the data for this chunk
		std::vector<unsigned char> generatedData(chunkSize);
		GenerateData(generatedData, filePath + std::to_string(segment));

		std::vector<unsigned char> fileData(chunkSize);
		unsigned long bytesRead;

		if (!::ReadFile(hFile, fileData.data(), chunkSize, &bytesRead, NULL) || bytesRead != chunkSize)
		{
			::CloseHandle(hFile);
			return false;
		}

		// Compare the read data with the generated data
		if (memcmp(fileData.data(), generatedData.data(), chunkSize) != 0)
		{
			::CloseHandle(hFile);
			return false;
		}

		totalBytesToRead -= chunkSize;
		offset += chunkSize;
		segment++;

		// Update progress, it starts at 50 and goes up to 100
		// TODO: Adjust this, verification is usually faster so it doens't truly reflect the progress in terms of speed
		CurrentProgress = 50 + (50 * (originalTotalBytesToRead - totalBytesToRead) / originalTotalBytesToRead);
		if (ProgressCallback != NULL)
			ProgressCallback(this, (int)State_Verification, CurrentProgress, 0);
	}

	::CloseHandle(hFile);

	return true;
}


bool DiskTest::PerformDestructiveTest()
{
	// TODO: Format the target disk, use all available space and perform test
	return false;
}

bool DiskTest::ForceStopTest()
{
	// Force stop if it's running
	if (TestRunning)
	{
		CurrentState = State_Aborted;
		TestRunning = false;
		return true;
	}

	return false;
}

int DiskTest::GetTestState()
{
	return CurrentState;
}

int DiskTest::GetTestProgress()
{
	return CurrentProgress;
}

double DiskTest::GetAverageReadSpeed()
{
	return AverageReadSpeed;
}

double DiskTest::GetAverageWriteSpeed()
{
	return AverageWriteSpeed;
}

unsigned long long DiskTest::GetLastSuccessfulVerifyPosition()
{
	return LastSuccessfulVerifyPosition;
}

std::string DiskTest::GenerateTestFileName()
{
	time_t now;
	time(&now);
	struct tm ltm;
	localtime_s(&ltm, &now);

	std::stringstream ss;

	// Generate a random test file name using hhmmss + something random, should be unique enough for our tests
	ss << 1900 + ltm.tm_year << 1 + ltm.tm_mon << ltm.tm_mday << ltm.tm_hour << ltm.tm_min << ltm.tm_sec << rand() % 1000 << ".tsc";

	return ss.str();
}

bool DiskTest::ReadAndVerifyData(HANDLE hFile, const std::vector<unsigned char>& generatedData, DWORD bytesWritten, unsigned long offset, double& totalReadDuration)
{
	std::vector<unsigned char> fileData(bytesWritten);
	DWORD bytesRead = 0;

	auto readStart = std::chrono::high_resolution_clock::now();
	// Read the block from the file
	if (!::ReadFile(hFile, fileData.data(), bytesWritten, &bytesRead, NULL) || bytesRead != bytesWritten)
		return false;
	auto readEnd = std::chrono::high_resolution_clock::now();

	std::chrono::duration<double, std::milli> durationMilliseconds = readEnd - readStart;
	totalReadDuration += durationMilliseconds.count();

	// Check if the data matches
	// This was too slow, replaced with memcmp
	if (memcmp(fileData.data(), generatedData.data() + offset, bytesWritten) != 0)
		return false;

	return true;
}

unsigned long DiskTest::WriteAndVerifyTestFile(const std::string& filePath, unsigned long long fileSize, bool failOnFirst)
{
	// FILE_FLAG_NO_BUFFERING is important
	HANDLE hFile = ::CreateFileA(filePath.c_str(), GENERIC_WRITE | GENERIC_READ, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, NULL);

	if (hFile == INVALID_HANDLE_VALUE)
		return 0;

	// Generate enough data to cover the entire file size initially
	std::vector<unsigned char> generatedData(MAX_RAND_DATA_SIZE);

	int segment = 0;

	// Generate initial data
	// TODO: Make this multi-threaded
	GenerateData(generatedData, filePath + std::to_string(segment));

	DWORD chunkSize = (DWORD)std::min<unsigned long long>(fileSize, DataBlockSize);

	// We always re-check the first written data chunk in order to attempt to find an error faster
	std::vector<unsigned char> firstGeneratedData(chunkSize);

	if (failOnFirst)
		std::memcpy(firstGeneratedData.data(), generatedData.data(), chunkSize);

	unsigned long long totalBytesGenerated = MAX_RAND_DATA_SIZE;
	unsigned long totalBytesWritten = 0;
	unsigned long offset = 0;

	double totalWriteDuration = 0;
	double totalReadDuration = 0;

	int operationCounter = 0;

	while (fileSize > 0 && TestRunning)
	{
		// If we've used up all our pre-generated data, generate more
		if (totalBytesGenerated < totalBytesWritten + chunkSize)
		{
			// TODO: Make this multi-threaded (pre-generate data while writing)
			segment++;
			GenerateData(generatedData, filePath + std::to_string(segment));
			totalBytesGenerated += MAX_RAND_DATA_SIZE;
		}

		DWORD bytesWritten = 0;

		auto writeStart = std::chrono::high_resolution_clock::now();
		if (!::WriteFile(hFile, generatedData.data() + offset, chunkSize, &bytesWritten, NULL))
			break;
		auto writeEnd = std::chrono::high_resolution_clock::now();

		std::chrono::duration<double, std::milli> durationMilliseconds = writeEnd - writeStart;
		totalWriteDuration += durationMilliseconds.count();

		// Flush the data to the disk
		::FlushFileBuffers(hFile);

		// Verify the data immediately after writing if failOnFirst is true
		if (failOnFirst)
		{
			// Set the file pointer back to the start of the written block
			::SetFilePointer(hFile, -static_cast<LONG>(bytesWritten), NULL, FILE_CURRENT);

			if (!ReadAndVerifyData(hFile, generatedData, bytesWritten, offset, totalReadDuration))
			{
				LastSuccessfulVerifyPosition += bytesWritten;
				break;
			}

			// Furthermore, we read and verify the first written data every single time, as it the most prone to corruption if this device is fake

			// Save current position
			LONG currentHighPart = 0;
			DWORD currentLowPart = ::SetFilePointer(hFile, 0, &currentHighPart, FILE_CURRENT);
			if (currentLowPart == INVALID_SET_FILE_POINTER && GetLastError() != NO_ERROR)
				break;

			// Set file pointer to the start of the file
			LONG zero = 0;
			::SetFilePointer(hFile, zero, &zero, FILE_BEGIN);

			// Re-read and verify the first data
			if (!ReadAndVerifyData(hFile, firstGeneratedData, chunkSize, 0, totalReadDuration))
				break;

			// Restore file pointer to the previous position
			::SetFilePointer(hFile, currentLowPart, &currentHighPart, FILE_BEGIN);

			MbWritten = LastSuccessfulVerifyPosition / (1024 * 1024);
		}
		else
			MbWritten += bytesWritten;


		fileSize -= bytesWritten;
		totalBytesWritten += bytesWritten;
		offset = (offset + bytesWritten) % MAX_RAND_DATA_SIZE;  // Wraparound the offset

		// Calculate speed in MB/s
		auto avgWriteSpeed = (totalBytesWritten / (totalWriteDuration / 1000.0)) / (1024 * 1024); // Convert ms to seconds
		auto avgReadSpeed = (totalBytesWritten / ((totalReadDuration / 2) / 1000.0)) / (1024 * 1024); // Convert ms to seconds

		AverageWriteSpeed = AverageWriteSpeed == 0 ? avgWriteSpeed : ((AverageWriteSpeed + avgWriteSpeed) / 2);
		AverageReadSpeed = AverageReadSpeed == 0 ? avgReadSpeed : ((AverageReadSpeed + avgReadSpeed) / 2);

		LastSuccessfulVerifyPosition += bytesWritten;

		// 50 because we write and verify
		CurrentProgress = (LastSuccessfulVerifyPosition * 50) / this->CapacityToTest;


		// Prevent spamming whatever is our callback
		if (operationCounter > 50)
		{
			if (ProgressCallback != NULL)
				ProgressCallback(this, (int)State_InProgress, CurrentProgress, MbWritten);

			operationCounter = 0;
		}
		else
			operationCounter++;
	}


	::CloseHandle(hFile);
	return totalBytesWritten;
}


unsigned long DiskTest::GetDataBlockSize(const std::string& path) {
	unsigned long sectorsPerCluster;
	unsigned long bytesPerSector;
	unsigned long numberOfFreeClusters;
	unsigned long totalNumberOfClusters;

	if (GetDiskFreeSpaceA(path.c_str(), &sectorsPerCluster, &bytesPerSector, &numberOfFreeClusters, &totalNumberOfClusters)) {
		return sectorsPerCluster * bytesPerSector;
	}
	else {
		return 0;
	}
}


bool DiskTest::GetDiskSpace(const std::string& path, unsigned long long* totalSpace, unsigned long long* freeSpace)
{
	unsigned long long availableSpace;

	if (GetDiskFreeSpaceExA(path.c_str(), (PULARGE_INTEGER)&availableSpace, (PULARGE_INTEGER)totalSpace, (PULARGE_INTEGER)freeSpace) == 0)
		return false;
	else
		return true;
}

bool DiskTest::IsDriveFull()
{
	unsigned long long totalSpace, freeSpace;

	if (GetDiskSpace(Path, &totalSpace, &freeSpace) == false)
	{
		// Error getting disk space, we just return true here
		return true;
	}

	// Check if the used space is greater than or equal to the max capacity
	if ((totalSpace - freeSpace) >= MaxCapacity)
		return true;

	return false;
}

//bool DiskTest::DeleteTestFile(const std::string& filePath)
//{
//	if (remove(filePath.c_str()) != 0)
//		return false;
//
//	return true;
//}