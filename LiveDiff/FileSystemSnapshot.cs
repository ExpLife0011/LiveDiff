﻿/*
LiveDiff: A portable system-level differencing tool
thomas@thomaslaurenson.com
https://github.com/thomaslaurenson/LiveDiff

Copyright(C) 2018 Thomas Laurenson 

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.If not, see<http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiveDiff
{
    class FileSystemSnapshot
    {
        public FileSystemSnapshot(String path)
        {
            RootPath = path;
            AllFiles = new ConcurrentDictionary<String, FileInformation>();
            AllDirs = new ConcurrentDictionary<String, FileInformation>();
            AllFSEntries = new ConcurrentDictionary<String, FileInformation>();
        }

        public string RootPath { get; set; }

        public ConcurrentDictionary<String, FileInformation> AllFiles { get; set; }

        public ConcurrentDictionary<String, FileInformation> AllDirs { get; set; }

        public ConcurrentDictionary<String, FileInformation> AllFSEntries { get; set; }

        int DirCounter = 0;

        int FileCounter = 0;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr hFindFile);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATAW
        {
            public FileAttributes dwFileAttributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public int nFileSizeHigh;
            public int nFileSizeLow;
            public int dwReserved0;
            public int dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        static IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        /// <summary>
        /// Processes all files/dirs in the top level target directory.
        /// </summary>
        /// <returns>
        /// Success/Failure.
        /// </returns>
        /// <param name="path">An existing file system directory.</param>
        public bool GetFileSystemSnapshot(string path)
        {
            List<String> directoryList = new List<String>();
            WIN32_FIND_DATAW findData;
            IntPtr findHandle = INVALID_HANDLE_VALUE;

            try
            {
                path = path.EndsWith(@"\") ? path : path + @"\";
                findHandle = FindFirstFileW(path + @"*", out findData);
                if (findHandle != INVALID_HANDLE_VALUE)
                {
                    do
                    {
                        // Skip current directory (.) and parent directory (..) symbols
                        if (findData.cFileName != "." && findData.cFileName != "..")
                        {
                            string fullPath = path + findData.cFileName;

                            // Check if this is a directory and not a symbolic link since symbolic links 
                            // could lead to repeated files and folders as well as infinite loops
                            bool isDirectory = findData.dwFileAttributes.HasFlag(FileAttributes.Directory);
                            bool isSymbolicLink = findData.dwFileAttributes.HasFlag(FileAttributes.ReparsePoint);
                            if (isDirectory && !isSymbolicLink)
                            {
                                // Process directory
                                //string fullPath = path + findData.cFileName;
                                directoryList.Add(fullPath);
                                // Query file system entry, populate and append to dict
                                FileInformation directoryInformation = PopulateFileInformation(findData, path);
                                AllDirs.TryAdd(directoryInformation.FullPath, directoryInformation);
                                AllFSEntries.TryAdd(directoryInformation.FullPath, directoryInformation);
                                // Increase directory count
                                Interlocked.Increment(ref DirCounter);
                                Console.Write("\rDirs: {0} Files: {1}", DirCounter, FileCounter);
                            }
                            else if (!isDirectory)
                            {
                                // Process file
                                FileInformation fileInformation = PopulateFileInformation(findData, path);
                                AllFiles.TryAdd(fileInformation.FullPath, fileInformation);
                                AllFSEntries.TryAdd(fileInformation.FullPath, fileInformation);
                                // Increase file count
                                Interlocked.Increment(ref FileCounter);
                                Console.Write("\rDirs: {0} Files: {1}", DirCounter, FileCounter);
                            }
                        }
                    }
                    // Process any subdirectories
                    while (FindNextFile(findHandle, out findData));
                    directoryList.AsParallel().ForAll(x =>
                    {
                        if (FindNextFileRecursive(x))
                        {

                        }
                    });
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("Caught exception while trying to enumerate a directory...");
                Console.WriteLine(exception.ToString());
                if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
                return false;
            }
            if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
            return true;
        }

        /// <summary>
        /// Recursively processes subdirectories and the contained files.
        /// </summary>
        /// <returns>
        /// Success/Failure.
        /// </returns>
        /// <param name="path">An existing file system directory.</param>
        /// <param name="files">List of files to process.</param>
        /// <param name="directories">List of directories to process.</param>
        private bool FindNextFileRecursive(string path)
        {
            List<String> directoryList = new List<String>();
            WIN32_FIND_DATAW findData;
            IntPtr findHandle = INVALID_HANDLE_VALUE;

            try
            {
                findHandle = FindFirstFileW(path + @"\*", out findData);
                if (findHandle != INVALID_HANDLE_VALUE)
                {
                    do
                    {
                        // Skip current directory (.) and parent directory (..) symbols
                        if (findData.cFileName != "." && findData.cFileName != "..")
                        {
                            string fullPath = path + @"\" + findData.cFileName;                        

                            // Check if this is a directory and not a symbolic link since symbolic links 
                            // could lead to repeated files and folders as well as infinite loops.
                            bool isDirectory = findData.dwFileAttributes.HasFlag(FileAttributes.Directory);
                            bool isSymbolicLink = findData.dwFileAttributes.HasFlag(FileAttributes.ReparsePoint);
                            if (isDirectory && !isSymbolicLink)
                            {
                                // Add the directory to the list
                                directoryList.Add(fullPath);

                                FileInformation directoryInformation = PopulateFileInformation(findData, path);
                                AllDirs.TryAdd(directoryInformation.FullPath, directoryInformation);
                                AllFSEntries.TryAdd(directoryInformation.FullPath, directoryInformation);

                                // Increase directory count
                                Interlocked.Increment(ref DirCounter);
                                Console.Write("\rDirs: {0} Files: {1}", DirCounter, FileCounter);

                                if (FindNextFileRecursive(fullPath))
                                {

                                }
                            }
                            else if (!isDirectory)
                            {
                                // Add the file to the list
                                FileInformation fileInformation = PopulateFileInformation(findData, path);
                                AllFiles.TryAdd(fileInformation.FullPath, fileInformation);
                                AllFSEntries.TryAdd(fileInformation.FullPath, fileInformation);
                                // Increase file count
                                Interlocked.Increment(ref FileCounter);
                                Console.Write("\rDirs: {0} Files: {1}", DirCounter, FileCounter);
                            }
                        }
                    }
                    while (FindNextFile(findHandle, out findData));
                }
            }
            catch (Exception exception)
            {
                Console.WriteLine("Caught exception while trying to enumerate a directory...");
                Console.WriteLine(exception.ToString());
                if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
                return false;
            }
            if (findHandle != INVALID_HANDLE_VALUE) FindClose(findHandle);
            return true;
        }

        public class FileInformation
        {
            public ushort FileAttribute;
            public DateTime LastWriteTime;
            public DateTime LastAccessTime;
            public DateTime CreationTime;
            public long FileSize;
            public string FullPath;
        }

        private static FileInformation PopulateFileInformation(WIN32_FIND_DATAW findData, String path)
        {
            FileInformation fileInformation = new FileInformation();

            fileInformation.FileAttribute = Convert.ToUInt16(findData.dwFileAttributes);
            fileInformation.LastWriteTime = findData.ftLastWriteTime.ToDateTime();
            fileInformation.LastAccessTime = findData.ftLastAccessTime.ToDateTime();
            fileInformation.CreationTime = findData.ftCreationTime.ToDateTime();
            //nFileSizeHigh * (MAXDWORD + 1)) +nFileSizeLow -- from MS DOCS
            long DWORDMAX = UInt32.MaxValue;
            fileInformation.FileSize = (findData.nFileSizeHigh * (DWORDMAX + 1)) + findData.nFileSizeLow;
            path = path.EndsWith(@"\") ? path : path + @"\";
            fileInformation.FullPath = path + findData.cFileName;

            return fileInformation;
        }

        public static string CompareFileSystemSnapshots(FileSystemSnapshot fsSnapshot1, FileSystemSnapshot fsSnapshot2)
        {
            // Determine new files
            Parallel.ForEach (fsSnapshot2.AllFSEntries, data =>
            {
                // Determine if any key file system entries have been added
                // Logic: Entry is present in Snapshot1 and not present in Snapshot2
                if (!fsSnapshot1.AllFSEntries.ContainsKey(data.Key))
                {
                    Console.WriteLine("New entry found: {0}", data.Key);
                }
            });

            // Determine removed files
            Parallel.ForEach(fsSnapshot1.AllFSEntries, data =>
            {
                // Determine if any key file system entries have been removed
                // Logic: Entry is present in Snapshot2 and not present in Snapshot1
                if (!fsSnapshot2.AllFSEntries.ContainsKey(data.Key))
                {
                    Console.WriteLine("Removed entry found: {0}", data.Key);
                }
            });

            // Determine modified files (size, modified date, access date)
            Parallel.ForEach(fsSnapshot2.AllFSEntries, data =>
            {
                // Determine if any key file system entries have been removed
                // Logic: Entry is present in Snapshot2 and not present in Snapshot1
                if (fsSnapshot1.AllFSEntries.ContainsKey(data.Key))
                {
                    FileInformation fi1 = fsSnapshot1.AllFSEntries[data.Key];
                    FileInformation fi2 = data.Value;
                    if (fi1.FileSize != fi2.FileSize)
                    {
                        Console.WriteLine("File size changed: {0}", fi2.FullPath);
                    }
                    if (fi1.LastAccessTime != fi2.LastAccessTime)
                    {
                        Console.WriteLine("Last access time changed: {0}", fi2.FullPath);
                    }
                    if (fi1.LastWriteTime != fi2.LastWriteTime)
                    {
                        Console.WriteLine("Last write time changed: {0}", fi2.FullPath);
                    }
                    if (fi1.FileAttribute != fi2.FileAttribute)
                    {
                        Console.WriteLine("File Attribute changed: {0}", fi2.FullPath);
                    }
                }
            });
            return "";
        }
    }

    public static class FILETIMEExtensions
    {
        public static DateTime ToDateTime(this System.Runtime.InteropServices.ComTypes.FILETIME time)
        {
            ulong high = (ulong)time.dwHighDateTime;
            ulong low = (ulong)time.dwLowDateTime;
            long fileTime = (long)((high << 32) + low);
            return DateTime.FromFileTimeUtc(fileTime);
        }
    }
}
