using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;
using Serilog.Sinks.MSSqlServer;

namespace LoggingPOC
{
    public class Program
    {
        public static void Main(string[] args)
        {

            Log.Logger = GetLoggerConfig().CreateLogger();

            try
            {
                Log.Information("Starting up");
                CreateHostBuilder(args).Build().Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application start-up failed");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        private static LoggerConfiguration GetLoggerConfig () 
        {
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json");

            IConfiguration configuration = builder.Build();

            var columnOptions = new ColumnOptions
            {
                AdditionalColumns = new Collection<SqlColumn>
                {
                    new SqlColumn("UserName", SqlDbType.VarChar)
                }
            };
            var log = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext();

            if (Convert.ToBoolean(configuration["File:IsActive"]))
            {
                log.WriteTo.File(configuration["File:Path"], rollingInterval: RollingInterval.Day);
            }
            if (Convert.ToBoolean(configuration["Database:IsActive"]))
            {
                log.WriteTo.MSSqlServer(configuration["Database:ConnectionString"], sinkOptions: new MSSqlServerSinkOptions { TableName = "Log" }
                    , null, null, LogEventLevel.Information, null, columnOptions: columnOptions, null, null);
            }
            if (Convert.ToBoolean(configuration["Seq:IsActive"]))
            {
                log.WriteTo.Seq(configuration["Seq:Url"]);
            }
            if (Convert.ToBoolean(configuration["Console:IsActive"]))
            {
                log.WriteTo.Console();
            }
            return log;
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                //.UseSerilog()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                });
    }
}
