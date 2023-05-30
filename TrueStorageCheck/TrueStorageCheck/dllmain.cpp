/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 */
// dllmain.cpp : Defines the entry point for the DLL application.
#include <windows.h>
#include <vector>
#include <string>
#include <windows.h>
#include <setupapi.h>
#include <devguid.h>
#include <initguid.h>
#include <cfgmgr32.h>
#include "DiskTest.hpp"

// Lazy me
#define EXPORT_C extern "C" __declspec(dllexport)
#define WRAP(expression) { return expression; }

// DLL Version
#define DLL_VERSION_MAJOR int(0x0)
#define DLL_VERSION_MINOR int(0x10)

BOOL APIENTRY DllMain(HMODULE hModule,
	DWORD  ul_reason_for_call,
	LPVOID lpReserved
)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

// Struct to hold device info, used by GetDevices
struct DeviceInfo
{
	wchar_t path[MAX_PATH + 1];
	wchar_t name[MAX_PATH + 1];
	wchar_t driveLetter;
	ULONGLONG capacity;
};


/// <summary>
/// Searches for all available devices and returns the struct about them in a dynamically allocated array
/// </summary>
/// <param name="includeLocalDisks"></param>
/// <param name="devices">strict as defined above</param>
/// <remarks>Drive letter is null (\0) is none</remarks>
/// <returns></returns>
EXPORT_C int GetDevices(bool includeLocalDisks, DeviceInfo **devices)
{
	// Create an empty vector to hold the DeviceInfo objects.
	std::vector<DeviceInfo> deviceVector;

	// Using MAX_PATH here just in case
	TCHAR terminatedVolumeName[MAX_PATH + 1];
	TCHAR deviceName[MAX_PATH + 1];

	// Volume name without trailing backslash
	TCHAR volumeName[MAX_PATH + 1] = { 0 };

	// Find the first volume name in the system and return a handle
	HANDLE hFind = FindFirstVolume(terminatedVolumeName, ARRAYSIZE(terminatedVolumeName));

	// No handle, no fun
	if (hFind == INVALID_HANDLE_VALUE)
	{
		*devices = nullptr;
		return 0;
	}

	// Iterate through all volume names
	do
	{
		// Get the length of the volume name and removes the trailing backslash (for QueryDosDevice......)
		size_t len = wcslen(terminatedVolumeName);
		for (size_t i = 0; i < len; i++)
			volumeName[i] = terminatedVolumeName[i];

		if (len > 1 && volumeName[len - 1] == L'\\')
			volumeName[len - 1] = L'\0';

		// Gets the device name associated with the volume using the QueryDosDevice function
		if (QueryDosDevice(&volumeName[4], deviceName, ARRAYSIZE(deviceName)) != 0)
		{
			// Creates a new DeviceInfo object and fills its fields with data
			DeviceInfo device;
			wcsncpy_s(device.path, volumeName, ARRAYSIZE(device.path));
			wcsncpy_s(device.name, deviceName, ARRAYSIZE(device.name));

			// Adds the drive letter if available
			TCHAR driveLetterPath[MAX_PATH * 2] = { 0 };
			if (GetVolumePathNamesForVolumeName(terminatedVolumeName, driveLetterPath, ARRAYSIZE(driveLetterPath), NULL))
				device.driveLetter = driveLetterPath[0];
			else
				device.driveLetter = L'\0';

			// Get the type of the volume (e.g., DRIVE_FIXED, DRIVE_REMOVABLE) using the GetDriveType function
			DWORD deviceType = GetDriveType(driveLetterPath);

			// Skip local disks if includeLocalDisks is false
			if (!includeLocalDisks && (deviceType == DRIVE_FIXED || deviceType == DRIVE_UNKNOWN || deviceType == DRIVE_NO_ROOT_DIR) || device.driveLetter == 'C' || device.driveLetter == 'c')
				continue;

			// Get the disk capacity
			ULARGE_INTEGER freeBytesAvailableToCaller, totalNumberOfBytes, totalNumberOfFreeBytes;
			if (GetDiskFreeSpaceEx(driveLetterPath, &freeBytesAvailableToCaller, &totalNumberOfBytes, &totalNumberOfFreeBytes))
			{
				// Capacity in bytes
				device.capacity = totalNumberOfBytes.QuadPart;  
			}
			else
				device.capacity = 0;

			// Adds the DeviceInfo object to the device vector
			deviceVector.push_back(device);
		}
	} while (FindNextVolume(hFind, terminatedVolumeName, ARRAYSIZE(volumeName)));

	// Closes the handle to the volume enumeration.
	FindVolumeClose(hFind);

	// Converts the device vector to a dynamic array and returns its length
	int deviceCount = static_cast<int>(deviceVector.size());
	*devices = new DeviceInfo[deviceCount];
	memcpy(*devices, deviceVector.data(), deviceCount * sizeof(DeviceInfo));
	return deviceCount;
}

EXPORT_C int GetMajorVersion() {
	return DLL_VERSION_MAJOR;
}

EXPORT_C int GetMinorVersion() {
	return DLL_VERSION_MINOR;
}

/// Just in case someone asks "Why didn't you do it in C++/CLI?!"
/// Because I like my programming languages like I like my coffee. Without unnecessary complexity.

#pragma region ExternadoAficionado

EXPORT_C DiskTest * DiskTest_Create(char driveLetter, unsigned long long capacityToTest, bool stopOnFirstError, bool deleteTempFiles, bool writeLogFile, DiskTest::ProgressDelegate callback)
{
	return new DiskTest(driveLetter, capacityToTest, stopOnFirstError, deleteTempFiles, writeLogFile, callback);
}

EXPORT_C void DiskTest_Destroy(DiskTest* instance) {

	instance->Dispose();
	delete instance;
}

// Bool seems to be non-blittable type and can't be used as a return value
EXPORT_C byte DiskTest_PerformTest(DiskTest* instance) WRAP(instance->PerformTest())
EXPORT_C byte DiskTest_PerformDestructiveTest(DiskTest* instance) WRAP(instance->PerformDestructiveTest())
EXPORT_C byte DiskTest_ForceStopTest(DiskTest* instance) WRAP(instance->ForceStopTest())
EXPORT_C int DiskTest_GetTestState(DiskTest* instance) WRAP(instance->GetTestState())
EXPORT_C int DiskTest_GetTestProgress(DiskTest* instance) WRAP(instance->GetTestProgress())
EXPORT_C int DiskTest_GetLastSuccessfulVerifyPosition(DiskTest* instance) WRAP(instance->GetLastSuccessfulVerifyPosition())
EXPORT_C double DiskTest_GetAverageWriteSpeed(DiskTest* instance) WRAP(instance->GetAverageWriteSpeed())
EXPORT_C double DiskTest_GetAverageReadSpeed(DiskTest* instance) WRAP(instance->GetAverageReadSpeed())
EXPORT_C long DiskTest_GetTimeRemaining(DiskTest* instance) WRAP(instance->GetTimeRemaining())
EXPORT_C byte DiskTest_IsDiskEmpty(DiskTest* instance) WRAP(instance->IsDiskEmpty())
EXPORT_C void DiskTest_DeleteTestFiles(DiskTest* instance) WRAP(instance->DeleteTestFiles())

#pragma endregion
