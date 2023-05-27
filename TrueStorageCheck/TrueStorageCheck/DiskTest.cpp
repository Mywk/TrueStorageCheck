/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
#include "DiskTest.hpp"

#include <ctime>
#include <random>
#include <fstream>
#include <sstream>
#include <algorithm>

 // Used for cleaning all files on the drive
#include <filesystem>

// Used for time calculations (Avg Read/Write speeds)
#include <chrono>

// Used for our multi-threading
#include <thread>
#include <iostream>

#define BYTES_TO_MB(x) (x / (1024 * 1024))

// For now we will always test in this block size, we write 512MB at a time
const unsigned long long DATA_WRITE_SIZE = 512 * (1024 * 1024);

// This is the maximum amount of random data we generate at a time
const unsigned long long MAX_RAND_DATA_SIZE = 64 * (1024 * 1024);

// Used for fast data generation
const int MAX_NUM_THREADS = std::thread::hardware_concurrency(); // Get number of supported concurrent threads

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
	BytesVerified = 0;

	TotalWriteDuration = 0;
	TotalReadDuration = 0;
}

void GenerateDataThread(std::vector<unsigned char>& data, size_t start, size_t end, unsigned long long seed)
{
	// LCG - 32b
	std::minstd_rand generator(seed);
	for (size_t i = start; i < end;)
	{
		unsigned int rand_val = generator();
		for (size_t j = 0; j < 4 && i < end; ++j, ++i)
		{
			data[i] = (rand_val >> (j * 8)) & 0xFF;
		}
	}
}

#pragma optimize( "s", on )
void DiskTest::GenerateData(std::vector<unsigned char>& data, const std::string& seed)
{
	// LCG - 32 - Multithreaded
	std::hash<std::string> hasher;
	unsigned long long hashed_seed = hasher(seed);
	std::minstd_rand seed_generator(hashed_seed);
	std::vector<std::thread> threads;

	// Adjust chunk size
	size_t chunk_size = (data.size() / MAX_NUM_THREADS);
	size_t remaining = data.size() % MAX_NUM_THREADS;

	for (int i = 0; i < MAX_NUM_THREADS; ++i)
	{
		size_t start = i * chunk_size;
		size_t end = (i != MAX_NUM_THREADS - 1) ? start + chunk_size : data.size();
		unsigned long long thread_seed = (static_cast<unsigned long long>(seed_generator()) << 32) | seed_generator();
		threads.push_back(std::thread(GenerateDataThread, std::ref(data), start, end, thread_seed));
	}

	for (auto& thread : threads)
		thread.join();
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

void DiskTest::CalculateProgress() {

	if (BytesWritten == 0 || BytesVerified == 0)
		CurrentProgress = 0;
	else
	{
		unsigned long long totalDataProcessed = BytesWritten + BytesVerified;
		unsigned long long totalDataToProcess = CapacityToTest + BytesToVerify;

		// Calculate the total data processed and total data to process
		CurrentProgress = (int)(((double)totalDataProcessed / totalDataToProcess) * 100);
	}

}

void DiskTest::WriteLogToFile(bool success) {

	std::filesystem::path filePath = Path + "TSC_Log_" + GetReadableDateTime() + ".txt";
	std::ofstream file(filePath);

	if (file.is_open()) {
		file << "Total Capacity:\t\t" << MaxCapacity << std::endl;
		file << "Verified Capacity:\t" << RealBytesVerified << std::endl;
		file << "Result:\t\t\t\t" << (success == true ? "Success" : (CurrentState == State_Aborted ? "Aborted" : "Failed")) << std::endl;
		file.close();
	}
}

byte DiskTest::PerformTest()
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

	unsigned long long freeSpace = 0;
	GetDiskSpace(Path, &this->MaxCapacity, &freeSpace);

	// Get the data block size for this disk, we attempt to read/write 50 blocks at a time
	DataBlockSize = GetDataBlockSize(Path);

	if (DataBlockSize == 0)
		return false;

	if (CapacityToTest == 0)
		CapacityToTest = freeSpace;


	// Ammount of data to write at a time
	unsigned long long sizeToWrite = std::min<unsigned long long>(this->CapacityToTest, DATA_WRITE_SIZE);

	// Calculate data to verify
	// These are proximate values only, this needs a better description (and possibly a better implementation)
	unsigned long long extraVerificationSize = floor(CapacityToTest / DATA_WRITE_SIZE) * DataBlockSize;
	BytesToVerify = StopOnFirstError ? ((CapacityToTest * 2) + extraVerificationSize) : CapacityToTest + (sizeToWrite * 3);

	unsigned long long totalDataWritten = 0;
	unsigned long long totalDataToWrite = CapacityToTest;

	unsigned long long dataLeftToWrite = CapacityToTest;

	if (freeSpace < totalDataToWrite)
		return false;

	if (ProgressCallback != NULL)
		ProgressCallback(this, (int)State_InProgress, CurrentProgress, BYTES_TO_MB(BytesWritten));

	bool ret = true;

	while (!IsDriveFull() && TestRunning && (totalDataWritten < totalDataToWrite))
	{
		if (dataLeftToWrite < sizeToWrite)
			sizeToWrite = dataLeftToWrite;

		std::string fileName = GenerateTestFileName();
		std::string filePath = Path + tempDirectoryPath + "\\" + fileName;

		// Write the test file
		auto dataWritten = WriteAndVerifyTestFile(filePath, sizeToWrite, StopOnFirstError);

		if (dataWritten < sizeToWrite)
		{
			BytesVerified = dataWritten;
			ret = false;
		}
		else
		{
			// Perform at least one complete read to get the Average Read speed for a better time calculation
			if (TestFiles.size() == 1)
				VerifyTestFile(TestFiles.front()->Path);
		}

		// If StopOnFirstError is true, every time we finish writing a file,
		// we check the first DataBlock from each file to ensure everything is still fine
		if (ret && StopOnFirstError)
		{
			for (const auto& testFile : TestFiles)
			{
				if (!InternalVerifyTestFile(testFile->Path, DataBlockSize, false, &testFile->Data[0]))
				{
					ret = false;
					break;
				}
			}
		}

		if (!ret)
			break;

		totalDataWritten += sizeToWrite;
		dataLeftToWrite -= sizeToWrite;

		if (ProgressCallback != NULL)
			ProgressCallback(this, (int)State_InProgress, CurrentProgress, BYTES_TO_MB(BytesWritten));
	}

	// Perform final verification
	if (ret && CurrentState != State_Aborted)
	{
		CurrentState = State_Verification;
		ProgressCallback(this, (int)State_Verification, CurrentProgress, BYTES_TO_MB(BytesVerified));

		for (const auto& testFile : TestFiles)
		{
			if (!VerifyTestFile(testFile->Path, true))
			{
				ret = false;
				break;
			}
		}
	}

	// Delete all the temporary files
	if (DeleteTempFiles)
	{
		// Delete any temporary data that can eventually already exist, then flush the changes
		this->RemoveDirectory(Path + tempDirectoryPath);

		// Write log file if needed
		if (WriteLogFile)
			WriteLogToFile(ret);
	}

	RecalculateAverageSpeeds();
	CalculateProgress();

	if (CurrentState != State_Aborted)
		CurrentState = ret ? State_Success : State_Error;

	if (ProgressCallback != NULL)
		ProgressCallback(this, CurrentState, CurrentProgress, BYTES_TO_MB(BytesWritten));

	TestRunning = false;

	return ret;
}

bool DiskTest::InternalVerifyTestFile(const std::string& filePath, unsigned long long fileSize, bool updateRealBytes, const unsigned char* pData)
{
	// FILE_FLAG_NO_BUFFERING is important
	HANDLE hFile = ::CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_NO_BUFFERING, NULL);

	if (hFile == INVALID_HANDLE_VALUE)
		return false;

	LARGE_INTEGER fSize;

	if (fileSize == 0)
	{
		if (!::GetFileSizeEx(hFile, &fSize))
		{
			::CloseHandle(hFile);
			return false;
		}

		fileSize = fSize.QuadPart;
	}

	unsigned long long totalBytesToRead = fileSize;
	unsigned long offset = 0;
	int segment = 0;

	while (totalBytesToRead > 0 && TestRunning)
	{
		unsigned long chunkSize = (unsigned long)std::min<unsigned long long>(totalBytesToRead, MAX_RAND_DATA_SIZE);

		// Ensure chunkSize is a multiple of the block size
		chunkSize = chunkSize - (chunkSize % DataBlockSize);

		// Re-generate the data for this chunk
		std::vector<unsigned char> generatedData(chunkSize);

		if (pData != nullptr)
			memcpy(&generatedData[0], pData, chunkSize);
		else
			GenerateData(generatedData, filePath + std::to_string(segment));

		std::vector<unsigned char> fileData(chunkSize);
		unsigned long bytesRead;

		auto readStart = std::chrono::high_resolution_clock::now();
		if (!::ReadFile(hFile, fileData.data(), chunkSize, &bytesRead, NULL) || bytesRead != chunkSize)
		{
			::CloseHandle(hFile);
			return false;
		}
		auto readEnd = std::chrono::high_resolution_clock::now();

		bool success = true;

		// Compare the read data with the generated data
		if (memcmp(fileData.data(), generatedData.data(), chunkSize) != 0)
		{
			// Get the exact position where it failed
			for (size_t i = 0; i < chunkSize; i++)
			{
				if (fileData.data()[i] != generatedData.data()[i])
				{
					BytesVerified += i;

					if (updateRealBytes)
						RealBytesVerified += i;

					break;
				}
			}

			::CloseHandle(hFile);
			return false;
		}
		else
		{
			// If we are performing non-standard verifications we do not count that time towards our total count
			if (pData == nullptr)
			{
				std::chrono::duration<double, std::milli> durationMilliseconds = readEnd - readStart;
				TotalReadDuration += durationMilliseconds.count();
				BytesVerified += chunkSize;

				if (updateRealBytes)
					RealBytesVerified += chunkSize;
			}
		}

		totalBytesToRead -= chunkSize;
		offset += chunkSize;
		segment++;

		// Recalculate and update progress 
		RecalculateAverageSpeeds();
		CalculateProgress();

		if (ProgressCallback != NULL)
			ProgressCallback(this, (int)State_Verification, CurrentProgress, BYTES_TO_MB(chunkSize));
	}

	::CloseHandle(hFile);

	return true;
}

bool DiskTest::VerifyTestFile(const std::string& filePath, bool updateRealBytes)
{
	return(InternalVerifyTestFile(filePath, 0, updateRealBytes));
}

byte DiskTest::IsDiskEmpty()
{
	bool isEmpty = std::filesystem::is_empty(Path);

	if (!isEmpty)
	{
		int entryCount = 0;

		try
		{
			// This could be improved but it's good enough for now
			for (const auto& entry : std::filesystem::recursive_directory_iterator(Path, std::filesystem::directory_options::skip_permission_denied))
			{
				// Skip folders or anything starting with "System Volume Information"
				if (entry.is_directory() || entry.path().wstring().find(L"System Volume Information") != std::wstring::npos)				continue;

				entryCount++;
				break; // Exit the loop after finding at least one entry
			}

			isEmpty = (entryCount == 0);
		}
		catch (const std::exception&)
		{
			isEmpty = false;
		}

	}

	return static_cast<byte>(isEmpty);
}


byte DiskTest::PerformDestructiveTest()
{
	// TODO: Format the target disk, use all available space and perform test
	return false;
}

byte DiskTest::ForceStopTest()
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
	return RealBytesVerified;
}

std::string DiskTest::GetReadableDateTime()
{
	time_t now;
	time(&now);
	struct tm ltm;
	localtime_s(&ltm, &now);

	std::stringstream ss;

	// Generate a random test file name using YYMMDD_hhmmss 
	ss << 1900 + ltm.tm_year << 1 + ltm.tm_mon << ltm.tm_mday << "_" << ltm.tm_hour << ltm.tm_min << ltm.tm_sec;

	return ss.str();
}

std::string DiskTest::GenerateTestFileName()
{
	std::stringstream ss;

	// Generate a random test file name using YYMMDDhhmmss + something random, should be unique enough for our tests
	ss << GetReadableDateTime() << rand() % 1000 << ".tsc";

	return ss.str();
}

void DiskTest::RecalculateAverageSpeeds()
{
	// Calculate speed in MB/s
	auto avgWriteSpeed = (BytesWritten / (TotalWriteDuration / 1000.0)) / (1024 * 1024); // Convert ms to seconds
	auto avgReadSpeed = (BytesVerified > 0 && TotalReadDuration > 0) ? (BytesVerified / (TotalReadDuration / 1000.0)) / (1024 * 1024) : 0; // Convert ms to seconds

	AverageWriteSpeed = AverageWriteSpeed == 0 ? avgWriteSpeed : ((AverageWriteSpeed + avgWriteSpeed) / 2);
	AverageReadSpeed = AverageReadSpeed == 0 ? avgReadSpeed : ((AverageReadSpeed + avgReadSpeed) / 2);
}

// At some point I should just re-write all this to use SCSI Read/Write when applicable
unsigned long DiskTest::WriteAndVerifyTestFile(const std::string& filePath, unsigned long long fileSize, bool failOnFirst)
{
	// FILE_FLAG_NO_BUFFERING is important
	HANDLE hFile = ::CreateFileA(filePath.c_str(), FILE_READ_DATA | FILE_WRITE_DATA, 0, NULL, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, NULL);

	if (hFile == INVALID_HANDLE_VALUE)
		return 0;

	unsigned long long chunkSize = std::min<unsigned long long>(fileSize, MAX_RAND_DATA_SIZE);

	// Create avector big enough data to cover the entire random data size initially
	std::vector<unsigned char> generatedData(chunkSize);

	unsigned int segment = 0;

	// Generate initial data
	GenerateData(generatedData, filePath + std::to_string(segment));

	// Ensure chunkSize is a multiple of the block size
	chunkSize = chunkSize - (chunkSize % DataBlockSize);

	unsigned long long fileBytesGenerated = chunkSize;
	unsigned long fileBytesWritten = 0;
	unsigned long offset = 0;

	TestFile* testFile = new TestFile(filePath, fileSize);

	TestFiles.push_back(testFile);

	while (fileSize > 0 && TestRunning)
	{
		// Remaining
		if (chunkSize > fileSize)
		{
			chunkSize = fileSize;
			generatedData.resize(chunkSize);
		}

		// If we've used up all our pre-generated data, generate more
		if (fileBytesGenerated < fileBytesWritten + chunkSize)
		{
			segment++;
			GenerateData(generatedData, filePath + std::to_string(segment));
			fileBytesGenerated += generatedData.size();
		}

		unsigned long bytesWritten = 0;

		auto writeStart = std::chrono::high_resolution_clock::now();
		if (!::WriteFile(hFile, generatedData.data() + offset, chunkSize, &bytesWritten, NULL)) {
			break;
		}
		auto writeEnd = std::chrono::high_resolution_clock::now();

		std::chrono::duration<double, std::milli> durationMilliseconds = writeEnd - writeStart;
		TotalWriteDuration += durationMilliseconds.count();
		BytesWritten += bytesWritten;

		// Flush the data to the disk - shouldn't be necessary but
		// a lot of drivers just lie to us and this seems to help
		::FlushFileBuffers(hFile);

		if (failOnFirst)
		{
			// If it's the first, save part of the generated data for our quick tests
			if (fileBytesWritten == 0)
				testFile->SetData((const unsigned char*)&generatedData[0], DataBlockSize);

			// We always read and verify the first written data every single time,
			// as it the most prone to corruption if this device is fake

			// Save current position
			LONG currentHighPart = 0;
			DWORD currentLowPart = ::SetFilePointer(hFile, 0, &currentHighPart, FILE_CURRENT);
			if (currentLowPart == INVALID_SET_FILE_POINTER && GetLastError() != NO_ERROR)
				break;

			// Okay listen, I don't like closing and re-opening the file either, but fact is,
			// some fake sticks are way easier to detect if we close and re-open the file
			// regardless of our flags or forced flushes.
			{
				::CloseHandle(hFile);

				hFile = ::CreateFileA(filePath.c_str(), FILE_READ_DATA | FILE_WRITE_DATA, 0, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL | FILE_FLAG_NO_BUFFERING | FILE_FLAG_WRITE_THROUGH, NULL);

				if (hFile == INVALID_HANDLE_VALUE)
					break;
			}

			// Re-read and verify the first written data
			std::vector<unsigned char> fileData(testFile->DataSize);
			unsigned long bytesRead = 0;

			// Read the block from the file
			if (!::ReadFile(hFile, fileData.data(), testFile->DataSize, &bytesRead, NULL) || bytesRead != testFile->DataSize)
				break;

			// Check if the data matches
			if (memcmp(fileData.data(), &testFile->Data[0], testFile->DataSize) != 0)
			{
				RealBytesVerified = BytesWritten;

				// Get near position where it failed - Not very precise, this could be improved
				for (size_t i = 0; i < chunkSize; i++)
				{
					if (fileData.data()[i] != testFile->Data[i])
					{
						RealBytesVerified += i;
						break;
					}
				}

				::CloseHandle(hFile);
				return false;
			}

			// Restore file pointer to the previous position
			::SetFilePointer(hFile, currentLowPart, &currentHighPart, FILE_BEGIN);

			BytesVerified += testFile->DataSize;
		}

		fileSize -= bytesWritten;
		fileBytesWritten += bytesWritten;
		offset = (offset + bytesWritten) % MAX_RAND_DATA_SIZE;

		// Recalculate average speeds and progress
		RecalculateAverageSpeeds();
		CalculateProgress();
		if (ProgressCallback != NULL)
			ProgressCallback(this, (int)State_InProgress, CurrentProgress, BYTES_TO_MB(BytesWritten));
	}

	if (hFile != INVALID_HANDLE_VALUE)
		::CloseHandle(hFile);

	return fileBytesWritten;
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

byte DiskTest::IsDriveFull()
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

unsigned long DiskTest::GetTimeRemaining()
{
	// Note: We can't just assume the read/write times are roughly equal, they almost never are,
	// especially in fake devices, so we need to calculate seperately and then add them together

	// Calculate remaining time for writing and reading separately, then sum
	int timeRemainingForWritingSec = AverageWriteSpeed == 0 ? 0 : ((CapacityToTest - BytesWritten) / (1024 * 1024)) / AverageWriteSpeed;
	int timeRemainingForReadingSec = AverageReadSpeed == 0 ? 0 : ((BytesToVerify - BytesVerified) / (1024 * 1024)) / AverageReadSpeed;


	// Assume double the write speed as default
	if (timeRemainingForReadingSec == 0)
		timeRemainingForReadingSec = AverageWriteSpeed == 0 ? 0 : ((CapacityToTest) / (1024 * 1024)) / (AverageWriteSpeed * 2);

	if (timeRemainingForWritingSec <= 0 || timeRemainingForReadingSec <= 0)
		return 0;

	return timeRemainingForWritingSec + timeRemainingForReadingSec;
}

void DiskTest::Dispose()
{
	// Clean up TestFiles
	for (TestFile* file : TestFiles) {
		delete file;
	}
}

//bool DiskTest::DeleteTestFile(const std::string& filePath)
//{
//	if (remove(filePath.c_str()) != 0)
//		return false;
//
//	return true;
//}