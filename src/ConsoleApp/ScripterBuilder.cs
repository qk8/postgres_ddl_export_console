using DdlExporter.Common;
using DdlExporter.Common.Configuration;
using DdlExporter.Common.Loggers;
using DdlExporter.Common.Writers;
using DdlExporter.Postgresql;
using DdlExporter.Mssql;
using System;


namespace mssql_ddl_export_console
{
    internal class ScripterBuilder
    {
        private string databaseType;
        private IConfigurationReader configurationReader;
        private IWriter writer;
        private ILogger logger;

        private ScripterBuilder(string databaseType)
        {
            this.databaseType = databaseType;
        }

        public static ScripterBuilder Get(string databaseType)
        {
            return new ScripterBuilder(databaseType);
        }

        public ScripterBuilder AddConfigurationReader(IConfigurationReader configurationReader)
        {
            this.configurationReader = configurationReader;
            return this;
        }
        public ScripterBuilder AddWriter(IWriter writer)
        {
            this.writer = writer;
            return this;
        }

        public ScripterBuilder AddLogger(ILogger logger)
        {
            this.logger = logger;
            return this;
        }

        private void Validate()
        {
            if (configurationReader == null)
                throw new ApplicationException("IConfigurationReader is not configured");
            if (writer == null)
                throw new ApplicationException("IWriter is not configured");
            if (logger == null)
                throw new ApplicationException("ILogger is not configured");
        }

        public IScripter Build()
        {
            Validate();
            switch (databaseType)
            {
                case MssqlScripter.DATABASE_TYPE:
                    return new MssqlScripter(this.configurationReader, writer, logger);
                case PostgresqlScripter.DATABASE_TYPE:
                    return new PostgresqlScripter(this.configurationReader, writer, logger);
            }
            throw new NotImplementedException();
        }

        public IScripter Build(IConfigurationReader configurationReader, IWriter writer, ILogger logger)
        {
            AddConfigurationReader(configurationReader);
            AddWriter(writer);
            AddLogger(logger);
            return Build();
        }
    }
}
