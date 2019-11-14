namespace Crowdin.Api
{
	public sealed class ProjectCredentials : Credentials
	{
		public string ProjectKey { get; set; }
		public string ProjectId { get; set; }
	}
}