/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
#pragma once

#include <windows.h>
#include <string>
#include <vector>

#include "TestFile.hpp"

class DiskTest
{
public:

	// Delegate for our progress event (int state, int progress, int mbWritten)
	typedef void(__stdcall* ProgressDelegate)(void*, int, int, int);

	/// <summary>
	/// DiskTest object constructor
	/// </summary>
	/// <param name="driveLetter">The disk to test</param>
	/// <param name="capacityToTest">Total capacity to test in MB, or 0 for testing all free space</param>
	/// <param name="stopOnFirstError">Should test stop on first error</param>
	/// <param name="deleteTempFiles">Should delete temporary files after test</param>
	/// <param name="writeLogFile">Should write test log to a file</param>
	DiskTest(char driveLetter, unsigned long long capacityToTest, bool stopOnFirstError, bool deleteTempFiles, bool writeLogFile, ProgressDelegate callback);


	/// <summary>
	/// Starts the normal disk test, this uses all of the parameters given on DiskTest
	/// </summary>
	/// <returns>Test started successfully</returns>
	byte PerformTest();

	/// <summary>
	/// Starts the destructive disk test, this will format the whole device before testing
	/// </summary>
	/// <returns>Test started successfully</returns>
	byte PerformDestructiveTest();

	/// <summary>
	/// Stops the disk test
	/// </summary>
	/// <returns>Test stopped successfully</returns>
	byte ForceStopTest();

	/// <summary>
	/// Gets the test state
	/// </summary>
	/// <returns>State code</returns>
	int GetTestState();

	/// <summary>
	/// Gets the test progress percentage.
	/// </summary>
	/// <returns>Percentage</returns>
	int GetTestProgress();

	/// <summary>
	/// Gets the average read speed.
	/// </summary>
	/// <returns>Speed/p/second</returns>
	double GetAverageReadSpeed();

	/// <summary>
	/// Gets the average write speed.
	/// </summary>
	/// <returns>Speed/p/second</returns>
	double GetAverageWriteSpeed();

	/// <summary>
	/// Gets the last position a write was successful
	/// </summary>
	/// <returns>Percentage</returns>
	unsigned long long GetLastSuccessfulVerifyPosition();

	/// <summary>
	/// Returns a string formatted as YYMMDDhhmmss
	/// </summary>
	/// <returns></returns>
	std::string GetReadableDateTime();

	/// <summary>
	/// Checks if disk is full.
	/// </summary>
	/// <returns>disk full state</returns>
	byte IsDriveFull();

	/// <summary>
	/// Gets the remaining time in seconds
	/// </summary>
	/// <remarks>
	/// This is just an estimate
	/// </remarks>
	unsigned long GetTimeRemaining();

	/// <summary>
	/// Checks if the disk is empty
	/// </summary>
	/// <returns></returns>
	byte IsDiskEmpty();

	/// <summary>
	/// Deletes all test files from the disk
	/// </summary>
	/// <warning>
	/// Deletes the whole TSC_Files directory
	/// </warning>
	/// <param name="filePath">Path</param>
	void DeleteTestFiles();


	/// <summary>
	/// Call before deleting
	/// </summary>
	void Dispose();

	/// <summary>
	/// Return states
	/// </summary>
	enum State
	{
		State_Waiting = 0,
		State_InProgress,
		State_Verification,
		State_Success,
		State_Error,
		State_Aborted
	};

private:

	/// <summary>
	/// Test path
	/// </summary>
	std::string Path;

	/// <summary>
	/// Options
	/// </summary>
	bool stopOnFirstError;
	bool deleteTempFiles;
	bool writeLogFile;

	/// <summary>
	/// Progress callback - To report progress
	/// </summary>
	ProgressDelegate progressCallback;

	/// <summary>
	/// Vector of created files
	/// </summary>
	std::vector<TestFile*> testFiles;

	/// <summary>
	/// Other variables
	/// </summary>
	unsigned long long maxCapacity;
	unsigned long long dataBlockSize;
	unsigned long long currentFileSize;

	State CurrentState;

	int CurrentProgress;

	unsigned long long capacityToTest;
	unsigned long long bytesWritten;
	unsigned long long bytesToVerify;

	// Bytes verified, this is the ammount regardless of uniqueness
	unsigned long long bytesVerified;

	// Unique bytes verified, used in the last verification
	unsigned long long bealBytesVerified;

	double totalWriteDuration;
	double totalReadDuration;

	double averageReadSpeed;
	double averageWriteSpeed;

	bool testRunning;


	/// <summary>
	/// Writes a test file to the disk
	/// </summary>
	/// <param name="filePath">Path</param>
	/// <param name="size">Size</param>
	/// <param name="failOnFirst">Fail on first try</param>
	/// <returns>Written verified position, or 0 if failed</returns>
	unsigned long WriteAndVerifyTestFile(const std::string& filePath, unsigned long long fileSize, bool failOnFirst);

	/// <summary>
	/// Call to update average read/write speeds using the data available
	/// </summary>
	void RecalculateAverageSpeeds();

	/// <summary>
	/// Generates a unique test file name
	/// </summary>
	/// <returns>Unique file name</returns>
	std::string GenerateTestFileName();

	/// <summary>
	/// Gets disk space
	/// </summary>
	/// <param name="path">Disk path</param>
	/// <param name="totalSpace">Total disk space</param>
	/// <param name="freeSpace">Free disk space</param>
	/// <returns>True if successfull retrieving the disk space</returns>
	bool GetDiskSpace(const std::string& diskPath, unsigned long long* totalSpace, unsigned long long* freeSpace);

	/// <summary>
	/// Verifies a test file on the disk - Regenerates the data using the filePath for checking
	/// </summary>
	/// <param name="filePath">Path</param>
	/// <param name="updateRealBytes">Updates the total/real number of valid bytes</param>
	/// <returns>File verified successfully</returns>
	bool VerifyTestFile(const std::string& filePath, bool updateRealBytes = false);

	/// <summary>
	/// Verifies a test file on the disk - Regenerates the data using the filePath for checking
	/// </summary>
	/// <param name="filePath">Path</param>
	/// <param name="fileSize">File size, or zero if it needs to be fetched</param>
	/// <param name="updateRealBytes">Updates the total/real number of valid bytes</param>			
	/// <param name="pData">Optional: For verifying data without generating</param>			
	/// <returns>File verified successfully</returns>
	bool InternalVerifyTestFile(const std::string& filePath, unsigned long long fileSize = 0, bool updateRealBytes = false, const unsigned char* pData = nullptr);

	/// <summary>
	/// Generate random data using a seed (mt19937 )
	/// </summary>
	/// <param name="data">Data</param>
	/// <param name="fileSize">size</param>
	void GenerateData(std::vector<unsigned char>& data, const std::string& seed);

	/// <summary>
	/// Deletes all files and directories on this Disk
	/// </summary>
	/// <warning>
	/// DANGER ZONE, NO CHECKS ARE MADE HERE!
	/// </warning>
	void DeleteAllFilesAndDirectories();

	/// <summary>
	/// Removes the given directory
	/// </summary>
	/// <param name="filePath"></param>
	void RemoveDirectory(const std::string& path);

	/// <summary>
	/// Retrieves the data block size of a disk by the specified path
	/// </summary>
	/// <param name="path">Path</param>
	/// <returns>The data block size in bytes or 0 if an error occurs</returns>
	unsigned long GetDataBlockSize(const std::string& path);

	/// <summary>
	/// Gets the given file size
	/// </summary>
	/// <param name="path">Path</param>
	unsigned long GetFileSize(const std::string& path);

	/// <summary>
	/// Used to updated CurrentProgress, MbWritten, MbToVerify values
	/// </summary>
	void CalculateProgress();

	/// <summary>
	/// Writes log file to tested disk
	/// </summary>
	/// <param name="totalCapacity"></param>
	/// <param name="verifiedCapacity"></param>
	/// <param name="successful"></param>
	void WriteLogToFile(bool success);

};