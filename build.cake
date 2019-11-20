#addin "nuget:?package=Cake.FileHelpers&version=3.2.1"

// user variable setting
var ID = "";
var NameSpace = "";
var ProjectName = "";
var SourceName = "";

// environment variable setting
var version = Argument("version", "");
var major = Argument("major", false);
var minor = Argument("minor", false);
var saveVersion = Argument("sv", false);
var name = Argument("name", ProjectName);
var target = Argument("target", "Create-Package");
var rootNs = Argument("namespace", NameSpace);
var configuration = Argument("configuration", "Debug");

// build variable setting
var projectFiles = GetFiles("*/**/*.csproj");
var buildPath = new DirectoryPath("build");
var artifactPath = new DirectoryPath("artifact");
var artifactFolder = buildPath.Combine(artifactPath);
string projectFilePath = null;
if(string.IsNullOrEmpty(name))
    projectFilePath = projectFiles.FirstOrDefault()?.FullPath;
else 
    projectFilePath = projectFiles.FirstOrDefault(file => file.GetFilenameWithoutExtension().FullPath.Contains(name))?.FullPath;
if(string.IsNullOrEmpty(projectFilePath)) 
    throw new Exception("no project found");
var projectPath = System.IO.Path.GetDirectoryName(projectFilePath);
var projectFolder = new DirectoryPath(projectPath);
var nuspecFile = GetFiles("source.nuspec").FirstOrDefault();

var releaseVersionFile = "release.version";
var debugVersionFile = "debug.version";
string autoVersionText = null;
string semVerText = null;
var isBeta = configuration == "Debug";

Task("Get-Version")
    .Description("get currect version for package")
    .Does(()=>
    {
        if(string.IsNullOrEmpty(version)) 
        {
            saveVersion = true;
            Version ver;
            if(TryGetCurrentVersion(releaseVersionFile, out ver)) 
            {                
                Version newVersion = null;                
                if(major)
                    newVersion = new Version(ver.Major + 1, ver.Minor, ver.Build, 0);
                else if(minor)
                    newVersion = new Version(ver.Major, ver.Minor + 1, ver.Build, 0);
                else
                    newVersion = new Version(ver.Major, ver.Minor, ver.Build + 1, 0);
                autoVersionText = newVersion.ToString(3);                
            }
            else 
                autoVersionText = "1.0.0";
            version = autoVersionText;            
            if(isBeta) 
            {
                semVerText = ReadText(debugVersionFile, "0");
                int semVer;
                int.TryParse(semVerText, out semVer);
                semVer++;
                semVerText = semVer.ToString();
                version += "-beta." + semVerText;
            }    
        }
        Information("Version: {0}", version);
        if(saveVersion) 
        {                  
            
            if(!isBeta) 
            {
                Information("save version: {0}, semVer: 0", autoVersionText); 
                FileWriteText(releaseVersionFile, autoVersionText);
                FileWriteText(debugVersionFile, "0");
            }
            else 
            {
                Information("save semVer: {0}", semVerText);
                FileWriteText(debugVersionFile, semVerText);
            }
        }
    });

Task("Clean-Artifact")
    .Description("clean artifact folder")
    .Does(()=>
    {
        Information("Cleaning {0}", artifactFolder);
        EnsureDirectoryExists(artifactFolder);
        CleanDirectory(artifactFolder);
    });

Task("Create-PPs")
    .Description("create source code (*.pp) for source NuGet pack")
    .Does(()=>
    {       
        CleanDirectory(artifactFolder);        
        CreatePartialSource("internal");        
        CreatePartialSource("public");
    });
    
Task("Create-Package")
    .Description("create nuget package")
    .IsDependentOn("Clean-Artifact")
    .IsDependentOn("Create-PPs")
    .IsDependentOn("Get-Version")
    .Does(()=>
    {   
        CreatePackage("internal");
        CreatePackage("public");
    });


RunTarget(target);

string ReadText(string file, string defaultText = null) 
{
    if(FileExists(file)) 
    {
        return FileReadText(file);
    }
    return defaultText;
}

bool TryGetCurrentVersion(string versionFile, out Version version) 
{    
    try 
    {        
        return Version.TryParse(FileReadText(versionFile), out version);
    }
    catch
    {
        version = new Version();
        return false;
    }
}
void CreatePartialSource(string accessibility) 
{
    var folder = artifactFolder.Combine(new DirectoryPath($".{accessibility}"));
    CreatePartialSource(accessibility, folder);
    CopyFileToDirectory(nuspecFile, folder);
}
void CreatePartialSource(string accessibility, DirectoryPath rootPath) 
{
    var compiledFiles = GetFiles("*/**/*.cs", info=>!info.Path.FullPath.EndsWith("obj", StringComparison.OrdinalIgnoreCase));
    var contentFolder = rootPath.Combine(new DirectoryPath($"contentFiles/any/any/{SourceName}"));
    foreach(var file in compiledFiles) 
    {
        if(file.GetFilenameWithoutExtension().FullPath == "AssemblyInfo")
            continue;
        var relativePath = projectFolder.GetRelativePath(file);
        var ppPath = contentFolder.CombineWithFilePath(relativePath).AppendExtension(".pp");
        Information("Convert file '{0}' to '{1}'", relativePath, ppPath);        
        EnsureDirectoryExists(ppPath.GetDirectory());
        var lines = FileReadLines(file);
        for(var lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            var line = lines[lineNum].Trim();
            if(line.StartsWith("[Accessibility"))
            {
                var newLine = lines[lineNum + 1].Replace("public", accessibility);
                lines[++lineNum] = newLine;
            }
            else if(line.StartsWith("namespace ")) 
            {
                lines[lineNum] = lines[lineNum].Replace(rootNs, "$rootnamespace$");
            }                
        }
        FileWriteLines(ppPath, lines);
    }        
}
void CreatePackage(string accessibility) 
{
    var folder = artifactFolder.Combine(new DirectoryPath($".{accessibility}"));
    var configFile = folder.GetFilePath(nuspecFile);
    var accessibilityText = accessibility.First().ToString().ToUpper() + accessibility.Substring(1);
    var id = $"{ID}.{accessibilityText}";
    Information("create {0} nuget package: {1} ({2})", accessibility, id, version);
    NuGetPack(configFile, new NuGetPackSettings
    {
        Id = id,
        Version = version,
        OutputDirectory = buildPath
    });        
}