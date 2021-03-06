#addin "nuget:?package=Cake.FileHelpers&version=3.2.1"

#load "variables.cake"


// user variable setting
var ID = GetText(_id, "");
var NameSpace = GetText(_nameSpace, "");
var ProjectName = GetText(_projectName, "");
var SourceName = GetText(_sourceName, "");
var ExcludeFileNames = _excludeFileNames;
var IncludeFileExtensions = _includeFileExtensions;
var Targets = _targets;


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


IEnumerable<FilePath> RecursiveGetFile(
    ICakeContext context,
    DirectoryPath directoryPath,
    string filter,
    Func<string, bool> predicate
    )
{
    var directory = context.FileSystem.GetDirectory(context.MakeAbsolute(directoryPath));
    foreach(var file in directory.GetFiles(filter, SearchScope.Current))
    {
        yield return file.Path;
    }
    foreach(var file in directory.GetDirectories("*.*", SearchScope.Current)
        .Where(dir=>predicate(dir.Path.FullPath))
        .SelectMany(childDirectory=>RecursiveGetFile(context, childDirectory.Path, filter, predicate))
        )
    {
        yield return file;
    }
}

List<FilePath> RecursiveGetFile(DirectoryPath directoryPath, string filter, Func<string, bool> predicate = null)
{
    if(predicate == null)
        predicate = path => true;
    return RecursiveGetFile(Context, directoryPath, filter, predicate).ToList();
}

string GetText(string text, string defaultText) {
    if(string.IsNullOrEmpty(text))
        return defaultText;
    return text;
}

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
    Information("create source: {0}", accessibility);
    var folder = artifactFolder.Combine(new DirectoryPath($".{accessibility}"));
    CreatePartialSource(accessibility, folder);
    CopyFileToDirectory(nuspecFile, folder);
}

void CreatePartialSource(string accessibility, DirectoryPath rootPath) 
{            
    if(Targets.Length > 1) 
    {
        var targets = Targets.ToDictionary(r=>r, r=>rootPath.Combine(new DirectoryPath($"contentFiles/{r.ToLower()}/{SourceName}")));
        CreateMultiCompiledSource(accessibility, targets);
        foreach(var extension in IncludeFileExtensions) {
            CreateMultiResources(targets, extension);
        }
    }
    else 
    {
        var contentFolder = rootPath.Combine(new DirectoryPath($"contentFiles/any/{SourceName}"));
        CreateCompiledSource(accessibility, contentFolder);
        foreach(var extension in IncludeFileExtensions) {
            CreateResources(contentFolder, extension);
        }
    }
}
void CreateMultiCompiledSource(string accessibility, Dictionary<string, DirectoryPath> ppRootPaths) 
{    
    var files = RecursiveGetFile(projectFolder, "*.cs", DirectionaryPredicate);
    Information("creating source code...{0}", files.Count());
    foreach(var file in files) 
    {
        var writeableMap = ppRootPaths.ToDictionary(r=>r.Key, r=>true);
        var relativePath = projectFolder.GetRelativePath(file).AppendExtension(".pp");        
        Information("Convert file '{0}'", relativePath);        
        var ppPaths = ppRootPaths.ToDictionary(r=>r.Key, r=>r.Value.CombineWithFilePath(relativePath));
        
        var lines = FileReadLines(file);
        var linesMap = ppRootPaths.ToDictionary(r=>r.Key, r=>new List<string>());
        for(var lineNum = 0; lineNum < lines.Length; lineNum++)
        {            
            var trimLine = lines[lineNum].TrimStart();
            if(trimLine.StartsWith("#if") || trimLine.StartsWith("#elif")) 
            {
                foreach(var key in Targets)
                    writeableMap[key] = ContainsTarget(trimLine, key);
                continue;
            }
            else if(trimLine.StartsWith("#else"))
            {
                foreach(var key in Targets)
                    writeableMap[key] = !writeableMap[key];
                continue;
            }
            else if(trimLine.StartsWith("#endif"))
            {
                foreach(var key in Targets)
                    writeableMap[key] = true;
                continue;
            }
            else if(trimLine.StartsWith("[Accessibility"))
            {                
                var newLine = lines[lineNum + 1].Replace("public", accessibility);
                lines[++lineNum] = newLine;
            }
            else if(trimLine.StartsWith("namespace ")) 
            {
                lines[lineNum] = lines[lineNum].Replace(rootNs, "$rootnamespace$");
            }
                    
            var writeKeys = writeableMap.Where(kv=>kv.Value).Select(kv=>kv.Key).ToArray();
            foreach(var key in writeKeys) 
            {
                var line = lines[lineNum];
                linesMap[key].Add(line);
            }
        }
        foreach(var ppPathKv in ppPaths) 
        {
            var ppPath = ppPathKv.Value;            
            var contentLines = linesMap[ppPathKv.Key].ToArray(); 
            EnsureDirectoryExists(ppPath.GetDirectory());       
            FileWriteLines(ppPath, contentLines);            
        }        
    }        
}

bool ContainsTarget(string line, string target) 
{
    line = line.ToUpper();
    target = target.ToUpper();
    var index = line.IndexOf(target);
    if(index == -1)
        return false;
    var inverseChar = line[index - 1];
    if(inverseChar == '!')
        return false;
    return true;
}
void CreateCompiledSource(string accessibility, DirectoryPath contentFolder) 
{    
    
    var files = RecursiveGetFile(projectFolder, "*.cs", DirectionaryPredicate);
    Information("creating source code...{0}", files.Count());
    foreach(var file in files) 
    {
        var relativePath = projectFolder.GetRelativePath(file);
        var ppPath = contentFolder.CombineWithFilePath(relativePath).AppendExtension(".pp");
        Information("Convert file '{0}' to '{1}'", relativePath, ppPath);        
        EnsureDirectoryExists(ppPath.GetDirectory());
        var lines = FileReadLines(file);
        for(var lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            var line = lines[lineNum];
            var trimLine = line.TrimStart();
            if(trimLine.StartsWith("[Accessibility"))
            {
                var newLine = lines[lineNum + 1].Replace("public", accessibility);
                lines[++lineNum] = newLine;
            }
            else if(trimLine.StartsWith("namespace ")) 
            {
                lines[lineNum] = lines[lineNum].Replace(rootNs, "$rootnamespace$");
            }
        }
        FileWriteLines(ppPath, lines);
    }        
}
void FileWriteLine(FilePath path, string line) 
{
    FileWriteText(path, line);
    FileWriteText(path, Environment.NewLine);
}
void FileWriteLine(IEnumerable<FilePath> paths, string line) 
{
    foreach(var path in paths) 
    {
        FileWriteLine(path, line);
    }
}

void CreateResources(DirectoryPath contentFolder, string extension) 
{    
    var files = RecursiveGetFile(projectFolder, $"*.{extension}", DirectionaryPredicate);
    Information("creating resource '{0}'...{1}", extension, files.Count());
    foreach(var file in files) 
    {
        var relativePath = projectFolder.GetRelativePath(file);
        var path = contentFolder.CombineWithFilePath(relativePath);
        Information("Copy file '{0}' to '{1}'", relativePath, path);        
        EnsureDirectoryExists(path.GetDirectory());
        CopyFile(file, path);
    }
}
void CreateMultiResources(Dictionary<string, DirectoryPath> ppRootPaths, string extension) 
{    
    var files = RecursiveGetFile(projectFolder, $"*.{extension}", DirectionaryPredicate);
    Information("creating resource '{0}'...{1}", extension, files.Count());
    foreach(var file in files) 
    {
        var relativePath = projectFolder.GetRelativePath(file);        
        Information("Copy file '{0}'", relativePath);        
        var paths = ppRootPaths.Values.Select(r=>r.CombineWithFilePath(relativePath));
        foreach(var path in paths) 
        {
            EnsureDirectoryExists(path.GetDirectory());
            CopyFile(file, path);
        }
    }
}
bool DirectionaryPredicate(string path) 
{
    return !path.EndsWith("obj", StringComparison.OrdinalIgnoreCase);
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