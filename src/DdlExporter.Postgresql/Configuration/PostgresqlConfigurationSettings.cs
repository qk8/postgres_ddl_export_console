using DdlExporter.Common.Configuration;

namespace DdlExporter.Postgresql.Configuration
{
    public class PostgresqlConfigurationSettings : ConfigurationSettings
    {
        public string ServerHost { get; set; }
        public int Port { get; set; } = 5432;
        public string DatabaseName { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string PgDumpPath { get; set; }
        public string PgRestorePath { get; set; }
    }
}