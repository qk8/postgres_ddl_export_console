using DdlExporter.Common;
using DdlExporter.Common.Configuration;
using DdlExporter.Common.Loggers;
using DdlExporter.Common.Writers;
using DdlExporter.Postgresql.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DdlExporter.Postgresql
{
    public class PostgresqlScripter : IScripter
    {
        public const string DATABASE_TYPE = "POSTGRESQL";

        private readonly PostgresqlConfigurationSettings _settings;
        private readonly IWriter _writer;
        private readonly ILogger _logger;

        public PostgresqlScripter(IConfigurationReader configurationReader, IWriter writer, ILogger logger)
        {
            _settings = configurationReader.Read<PostgresqlConfigurationSettings>();
            _writer = writer;
            _logger = logger;
        }

        public void Execute()
        {
            _logger.Log("PostgreSQL DDL Dışa Aktarma İşlemi Başladı...");

            var pgDumpExe = string.IsNullOrWhiteSpace(_settings.PgDumpPath) ? "pg_dump" : _settings.PgDumpPath;
            
            string pgRestoreExe = "pg_restore";
            if (!string.IsNullOrWhiteSpace(_settings.PgRestorePath))
            {
                pgRestoreExe = _settings.PgRestorePath;
            }
            else if (!string.IsNullOrWhiteSpace(_settings.PgDumpPath))
            {
                var dir = Path.GetDirectoryName(_settings.PgDumpPath);
                pgRestoreExe = Path.Combine(dir ?? "", "pg_restore");
                if (_settings.PgDumpPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    pgRestoreExe += ".exe";
                }
            }
            
            var tempArchiveFile = Path.GetTempFileName();
            var arguments = $"--host=\"{_settings.ServerHost}\" --port={_settings.Port} --username=\"{_settings.Username}\" --dbname=\"{_settings.DatabaseName}\" -Fc -s -f \"{tempArchiveFile}\"";

            _logger.Log($"pg_dump çalıştırılıyor: {pgDumpExe} {arguments}");

            try
            {
                RunProcess(pgDumpExe, arguments, _settings.Password);

                _logger.Log("TOC (Table of Contents) alınıyor...");
                var tocOutput = RunProcessAndReadOutput(pgRestoreExe, $"--list \"{tempArchiveFile}\"", _settings.Password);

                var tocLines = tocOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var tocEntries = ParseToc(tocLines);

                foreach (var entry in tocEntries)
                {
                    var listFile = Path.GetTempFileName();
                    try
                    {
                        File.WriteAllText(listFile, entry.OriginalLine);
                        var ddl = RunProcessAndReadOutput(pgRestoreExe, $"-f - --use-list=\"{listFile}\" \"{tempArchiveFile}\"", _settings.Password);
                        
                        if (!string.IsNullOrWhiteSpace(ddl) && !IsOnlyComments(ddl))
                        {
                            var safeSchema = string.Join("_", entry.Schema.Split(Path.GetInvalidFileNameChars())).Trim();
                            var safeName = string.Join("_", entry.Name.Split(Path.GetInvalidFileNameChars())).Trim();
                            
                            var objectNameWithSchema = string.IsNullOrEmpty(safeSchema) || safeSchema == "-" ? safeName : $"{safeSchema}.{safeName}";
                            var safeType = string.Join("_", entry.Type.Split(Path.GetInvalidFileNameChars())).Trim();
                            
                            _writer.Start(_settings.DatabaseName, safeType, objectNameWithSchema);
                            _writer.WriteLine(ddl.Trim());
                            _writer.Finish();
                            
                            _logger.Log($"Dışa aktarıldı: [{safeType}] {objectNameWithSchema}");
                        }
                    }
                    finally
                    {
                        if (File.Exists(listFile)) File.Delete(listFile);
                    }
                }
            }
            finally
            {
                if (File.Exists(tempArchiveFile))
                {
                    File.Delete(tempArchiveFile);
                }
            }

            _logger.Log("PostgreSQL DDL Dışa Aktarma İşlemi Tamamlandı.");
        }

        private bool IsOnlyComments(string ddl)
        {
            var lines = ddl.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.Length > 0 && !trimmed.StartsWith("--"))
                {
                    return false;
                }
            }
            return true;
        }

        private void RunProcess(string fileName, string arguments, string password = null)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(password))
            {
                processStartInfo.EnvironmentVariables["PGPASSWORD"] = password;
            }

            using (var process = new Process { StartInfo = processStartInfo })
            {
                var errorBuilder = new StringBuilder();

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"{fileName} başlatılamadı. Hata: {ex.Message}", ex);
                }

                process.BeginErrorReadLine();
                
                using (var reader = process.StandardOutput)
                {
                    reader.ReadToEnd();
                }

                if (!process.WaitForExit(3600000))
                {
                    try { process.Kill(); } catch { }
                    throw new ApplicationException($"{fileName} işlemi zaman aşımına uğradı.");
                }

                if (process.ExitCode != 0)
                {
                    var errors = errorBuilder.ToString();
                    throw new ApplicationException($"{fileName} çalıştırılırken bir hata oluştu. Exit Code: {process.ExitCode}. Hata Detayı: {errors}");
                }
            }
        }

        private string RunProcessAndReadOutput(string fileName, string arguments, string password = null)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrEmpty(password))
            {
                processStartInfo.EnvironmentVariables["PGPASSWORD"] = password;
            }

            using (var process = new Process { StartInfo = processStartInfo })
            {
                var errorBuilder = new StringBuilder();

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                };

                try
                {
                    process.Start();
                }
                catch (Exception ex)
                {
                    throw new ApplicationException($"{fileName} başlatılamadı. Hata: {ex.Message}", ex);
                }

                process.BeginErrorReadLine();
                
                string output;
                using (var reader = process.StandardOutput)
                {
                    output = reader.ReadToEnd();
                }

                if (!process.WaitForExit(3600000))
                {
                    try { process.Kill(); } catch { }
                    throw new ApplicationException($"{fileName} işlemi zaman aşımına uğradı.");
                }

                if (process.ExitCode != 0)
                {
                    var errors = errorBuilder.ToString();
                    throw new ApplicationException($"{fileName} çalıştırılırken bir hata oluştu. Exit Code: {process.ExitCode}. Hata Detayı: {errors}");
                }

                return output;
            }
        }

        private List<TocEntry> ParseToc(string[] lines)
        {
            var entries = new List<TocEntry>();
            var regex = new Regex(@"^(?<archiveId>\d+);\s+(?<objectId>[\d\s]+)\s+(?<type>[A-Z_ ]+)\s+(?<schema>\S+)\s+(?<name>.+?)\s+(?<owner>\S+)$", RegexOptions.Compiled);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(";"))
                    continue;

                var match = regex.Match(line);
                if (match.Success)
                {
                    var type = match.Groups["type"].Value.Trim();
                    
                    if (type != "ENCODING" && type != "STDSTRINGS" && type != "SEARCHPATH")
                    {
                        entries.Add(new TocEntry
                        {
                            ArchiveId = match.Groups["archiveId"].Value,
                            Type = type,
                            Schema = match.Groups["schema"].Value == "-" ? "" : match.Groups["schema"].Value,
                            Name = match.Groups["name"].Value.Trim(),
                            OriginalLine = line
                        });
                    }
                }
                else
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 6 && parts[0].EndsWith(";"))
                    {
                        var archiveId = parts[0].TrimEnd(';');
                        var type = parts[3];
                        var schema = parts[4] == "-" ? "" : parts[4];
                        var name = string.Join(" ", parts, 5, parts.Length - 6);

                        entries.Add(new TocEntry
                        {
                            ArchiveId = archiveId,
                            Type = type,
                            Schema = schema,
                            Name = name,
                            OriginalLine = line
                        });
                    }
                }
            }
            return entries;
        }

        private class TocEntry
        {
            public string ArchiveId { get; set; }
            public string Type { get; set; }
            public string Schema { get; set; }
            public string Name { get; set; }
            public string OriginalLine { get; set; }
        }
    }
}
