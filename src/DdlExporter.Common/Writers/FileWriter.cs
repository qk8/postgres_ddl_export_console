using DdlExporter.Common.Loggers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace DdlExporter.Common.Writers
{
    public class FileWriterOptions
    {
        public bool DirectoryByObjectType { get; set; } = true;
        public bool ObjectsInSeparateFile { get; set; } = true;
    }

    public class FileWriter : WriterBase, IDisposable
    {
        protected string BaseDirectoryPath { get; }
        protected FileWriterOptions Options { get; }
        private StreamWriter streamWriter;
        private bool disposedValue;

        public FileWriter(string baseDirectoryPath, ILogger logger) : this(baseDirectoryPath, new FileWriterOptions(), logger)
        {
        }
        public FileWriter(string baseDirectoryPath, FileWriterOptions options, ILogger logger) : base(logger)
        {
            BaseDirectoryPath = baseDirectoryPath;
            Options = options;
        }

        private string BuildPath(string databaseName, string objectType, string objectName)
        {
            var path = $"{BaseDirectoryPath}";
            path = Path.Combine(path, databaseName);
            if (Options.DirectoryByObjectType)
            {
                path = Path.Combine(path, objectType);
            }

            if (Directory.Exists(path) == false)
                Directory.CreateDirectory(path);

            if (Options.ObjectsInSeparateFile)
            {
                var safeObjectName = string.Join("_", objectName.Split(Path.GetInvalidFileNameChars()));
                path = Path.Combine(path, $"{safeObjectName}.sql");
            }
            else
            {
                path = Path.Combine(path, "ALL_OBJECTS.sql");
            }
            Logger.Log(path);
            return path;
        }

        public override void Finish()
        {
            streamWriter?.Flush();
            streamWriter?.Dispose();
        }

        public override void Start(string databaseName, string objectType, string objectName)
        {
            var path = BuildPath(databaseName, objectType, objectName);
            if (Options.ObjectsInSeparateFile)
            {
                streamWriter = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.ReadWrite));
            }
            else
            {
                streamWriter = new StreamWriter(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite));
            }
        }

        public override void WriteLine(string content)
        {
            streamWriter.WriteLine(content);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    streamWriter?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
