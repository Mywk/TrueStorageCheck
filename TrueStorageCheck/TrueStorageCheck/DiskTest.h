#pragma once
#include <string>
#include <vector>
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
	bool PerformTest();

	/// <summary>
	/// Starts the destructive disk test, this will format the whole device before testing
	/// </summary>
	/// <returns>Test started successfully</returns>
	bool PerformDestructiveTest();

	/// <summary>
	/// Stops the disk test
	/// </summary>
	/// <returns>Test stopped successfully</returns>
	bool ForceStopTest();

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
	/// Checks if disk is full.
	/// </summary>
	/// <returns>disk full state</returns>
	bool IsDriveFull();

	enum State
	{
		State_Waiting = 0,
		State_InProgress = 1,
		State_Verification = 2,
		State_Success = 3,
		State_Error = 4
	};

private:

	std::string Path;

	bool StopOnFirstError;
	bool DeleteTempFiles;
	bool WriteLogFile;

	ProgressDelegate ProgressCallback;

	unsigned long long LastSuccessfulVerifyPosition;

	unsigned long long MaxCapacity;
	unsigned long long CapacityToTest;

	unsigned long long DataBlockSize;

	double AverageReadSpeed;
	double AverageWriteSpeed;

	unsigned long long CurrentFileSize;
	std::vector<std::string> CreatedFiles;

	State CurrentState;
	int CurrentProgress;
	int MbWritten;

	bool TestRunning;

	/// <summary>
	/// Writes a test file to the disk
	/// </summary>
	/// <param name="filePath">Path</param>
	/// <param name="size">Size</param>
	/// <param name="failOnFirst">Fail on first try</param>
	/// <returns>Written verified position, or 0 if failed</returns>
	unsigned long WriteAndVerifyTestFile(const std::string& filePath, unsigned long long fileSize, bool failOnFirst);

	/// <summary>
	/// Used to verify a written file
	/// </summary>
	/// <param name="hFile">File handle</param>
	/// <param name="generatedData">A reference to the vector of generated data to verify against</param>
	/// <param name="bytesWritten">Length of bytes to read and verify</param>
	/// <param name="offset">The offset into the generated data buffer to begin verification</param>
	/// <param name="totalReadDuration">A reference to a double that accumulates the total read duration</param>
	/// <returns>True if the read and verification was ok</returns>
	bool ReadAndVerifyData(HANDLE hFile, const std::vector<unsigned char>& generatedData, DWORD bytesWritten, unsigned long offset, double& totalReadDuration);

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
	/// Deletes a single test file from the disk - No longer used
	/// </summary>
	/// <param name="filePath">Path</param>
	/// <returns>True if deleted successfully</returns>
	//bool DeleteTestFile(const std::string& filePath);

	/// <summary>
	/// Verifies a test file on the disk - Regenerates the data using the filePath for checking
	/// </summary>
	/// <param name="filePath">Path</param>
	/// <returns>File verified successfully</returns>
	bool VerifyTestFile(const std::string& filePath);

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

};