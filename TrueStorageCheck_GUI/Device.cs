/* Copyright (C) 2023 - Mywk.Net
 * Licensed under the EUPL, Version 1.2
 * You may obtain a copy of the Licence at: https://joinup.ec.europa.eu/community/eupl/og_page/eupl
 * Unless required by applicable law or agreed to in writing, software distributed under the Licence is distributed on an "AS IS" basis, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * 
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrueStorageCheck_GUI
{
    public class Device
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public char DriveLetter { get; set; }
        public ulong Capacity { get; set; }

        /// <summary>
        /// Possible device options
        /// </summary>
        public enum Option
        {
            StopOnFirstFailure,
            RemoveTempFilesWhenDone,
            SaveLogToMedia
        }

        /// <summary>
        /// Selected options
        /// </summary>
        public Option Options { get; set; } = Option.StopOnFirstFailure | Option.RemoveTempFilesWhenDone | Option.SaveLogToMedia;

        public override string ToString()
        {
            return DriveLetter + ":\\";
        }

        public Device(string name, string path, char driveLetter, ulong capacity)
        {
            Name = name;
            Path = path;
            DriveLetter = driveLetter;
            Capacity=capacity;
        }


        public void UpdateDevice(Device device)
        {
            Name = device.Name;
            DriveLetter = device.DriveLetter;
            Capacity = device.Capacity;
        }

        public void UpdateDevice(string newName, char newDriveLetter, ulong newCapacity)
        {
            Name = newName;
            DriveLetter = newDriveLetter;
            Capacity = newCapacity;
        }
    }
}
