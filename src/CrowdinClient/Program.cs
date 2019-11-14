using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using Crowdin.Api;
using Crowdin.Api.Typed;
using Microsoft.Extensions.Configuration;

namespace CrowdinClient
{
	internal sealed class Program
	{
		private Program(IConfiguration config)
		{
			Configuration = config;
		}

		private IConfiguration Configuration { get; }
		/// <summary>
		/// Because the async lambda functions return immediately we need a semaphore to make sure we wait for the
		/// Crowdin response on any Crowdin api calls before we exit.
		/// </summary>
		static readonly AutoResetEvent Gate = new AutoResetEvent(false);
		private static int Main(string[] args)
		{
			IConfiguration config = new ConfigurationBuilder()
				.AddJsonFile("appsettings.json", true, false)
				.AddEnvironmentVariables()
				.AddCommandLine(args)
				.Build();
			var program = new Program(config);
			int result = 1;
			var parseResult = Parser.Default.ParseArguments<UpdateOptions, AddOptions>(args)
				.WithParsed<UpdateOptions>(async opts =>
				{
					result = await program.UpdateFilesInCrowdin(opts);
				})
				.WithParsed<AddOptions>(async opts => await program.AddFilesToCrowdin(opts)).WithNotParsed(errs =>
				{
					Gate.Set();
				});
			Gate.WaitOne();
			return parseResult.Tag is ParserResultType.NotParsed ? 1 : result;
		}

		private async Task<int> UpdateFilesInCrowdin(UpdateOptions opts)
		{
			var httpClient = new HttpClient {BaseAddress = new Uri(Configuration["api"])};
			var crowdin = new Client(httpClient);

			var projectCredentials = GetConfigValue<ProjectCredentials>("project");
			var updateFileParameters = BuildUpdateFileParameters(opts);
			ConsoleOutput("Updating files...");
			var result = await crowdin.UpdateFile(projectCredentials.ProjectId,
				projectCredentials, updateFileParameters);
			if (result.IsSuccessStatusCode)
			{
				ConsoleOutput("Finished Updating files.");
			}
			else
			{
				ConsoleOutput("Failure updating files.");
				string error = await result.Content.ReadAsStringAsync();
				ConsoleOutput(error);
			}
			Gate.Set();
			return result.IsSuccessStatusCode ? 0 : 1;
		}

		private UpdateFileParameters BuildUpdateFileParameters(UpdateOptions opts)
		{
			var files = new Dictionary<string, FileInfo>();
			if (opts.Files.Any())
				foreach (var file in opts.Files)
					files[Path.GetFileName(file)] = new FileInfo(file);
			else
				foreach (var file in Configuration["files"].Split(";"))
					files[Path.GetFileName(file)] = new FileInfo(file);

			return new UpdateFileParameters {Files = files};
		}

		private async Task<int> AddFilesToCrowdin(AddOptions opts)
		{
			var httpClient = new HttpClient {BaseAddress = new Uri(Configuration["api"])};
			var crowdin = new Client(httpClient);

			var files = new Dictionary<string, FileInfo>();
			if (opts.Files.Any())
				foreach (var file in opts.Files)
					files[Path.GetFileName(file)] = new FileInfo(file);

			var projectCredentials = GetConfigValue<ProjectCredentials>("project");
			ConsoleOutput("Adding files");
			var result = await crowdin.AddFile(projectCredentials.ProjectId,
				projectCredentials, new AddFileParameters { Files = files });
			ConsoleOutput("Done adding files");
			ConsoleOutput(result.Content);
			Gate.Set();
			return result.IsSuccessStatusCode ? 0 : 1;
		}

		private async Task Run()
		{
			var httpClient = new HttpClient {BaseAddress = new Uri(Configuration["api"])};
			var crowdin = new Client(httpClient);

			//ConsoleWriteMessage("Press [Enter] to list Crowdin supported languages");
			//ReadOnlyCollection<LanguageInfo> languages = await crowdin.GetSupportedLanguages();
			//ConsoleOutput(languages);

			//ConsoleWriteMessage("Press [Enter] to list account projects");
			var accountCredentials = GetConfigValue<AccountCredentials>("account");
			//ReadOnlyCollection<AccountProjectInfo> accountProjects = await crowdin.GetAccountProjects(accountCredentials);
			//ConsoleOutput(accountProjects);

			ProjectInfo project;
			var projectCredentials = GetConfigValue<ProjectCredentials>("project");
			try
			{
				ConsoleWriteMessage("Press [Enter] to get project information using project API key");
				project = await crowdin.GetProjectInfo(projectCredentials.ProjectId, projectCredentials);
				ConsoleOutput(project);
			}
			catch
			{
				ConsoleOutput("Error using GetProjectInfo with project credentials");
			}


			ConsoleWriteMessage("Press [Enter] to get project information using account API key");
			project = await crowdin.GetProjectInfo(projectCredentials.ProjectId, accountCredentials);
			ConsoleOutput(project);

			ConsoleWriteMessage("Press [Enter] to get project translation status");
			var projectTranslationStatus =
				await crowdin.GetProjectStatus(projectCredentials.ProjectId, accountCredentials);
			ConsoleOutput(projectTranslationStatus);

			ConsoleWriteMessage("Press [Enter] to get language translation status");
			var getLanguageStatusParameters = new GetLanguageStatusParameters
			{
				Language = "fr"
			};
			var languageTranslationStatus = await crowdin.GetLanguageStatus(projectCredentials.ProjectId,
				accountCredentials, getLanguageStatusParameters);
			foreach (var file in languageTranslationStatus.Files)
				ConsoleOutput($"{file.Name}  is {file.WordsTranslated / file.WordsApproved:P}");
		}

		private T GetConfigValue<T>(string key)
		{
			return Configuration.GetSection(key).Get<T>();
		}

		private static void ConsoleWriteMessage(string message)
		{
			Console.WriteLine();
			Console.WriteLine(message);
			Console.ReadLine();
		}

		private static void ConsoleOutput(object value)
		{
			Console.WriteLine(value);
		}

		private static void ConsoleOutput<T>(IList<T> values)
		{
			var outputItems = Math.Min(3, values.Count);
			for (var i = 0; i < outputItems; i++) ConsoleOutput(values[i]);

			var restItems = values.Count - outputItems;
			if (restItems > 0) Console.WriteLine($"...({restItems} more items)");
		}

		[Verb("updatefiles", HelpText = "Update files in Crowdin. Will use crowdin.json or files passed in as arguments")]
		private class UpdateOptions
		{
			[Option('f', "file", Required = false, HelpText = "Path to a file to upload")]
			public IEnumerable<string> Files { get; set; }

			// TODO: Add option for update approval -- default to Update_as_unapproved
		}

		[Verb("addfiles", HelpText = "Add files to Crowdin. Will update crowdin.json if present")]
		private class AddOptions
		{
			[Option('f', "file", Required = false, HelpText = "Path(s) to a file to upload")]
			public IEnumerable<string> Files { get; set; }
		}
	}
}