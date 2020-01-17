#addin "nuget:?package=Cake.FileHelpers&version=3.2.1"

// =======================
//   Defined variables 
// =======================
string Id = "";                  // the source code id
string NameSpacePattern = "";    // the source code namespace to replace 
string SourceName = "";          // the source code name for package installed into
string RootPath = "";            // root path contains source files
string TempPath = "";            // temp path to put some temporary files, will clean after build success
string NuSpecPath = "";          // nuspec file path
string ArtifactPath = "";        // artifact output path
string ToolsPath = "";           // tools path
string[] ExcludeSources = { };     // exclude file list
string[] ExcludeSourceRegex = { }; // exclude file pattern list
string[] IncludeExtensions = { };     // include file list
string[] Targets = { };          // target platform (NET40;NETSTANDARD2_0;etc)
bool _isRelease = false;
string _target = "";
string _configuration = "";
string _accessibility = "";
string _version = "";
bool _isAzureDevOps = false;
bool _nextVersion = false;

// =======================
//   Load arguments
// =======================
Id = Argument("id", "");
NameSpacePattern = Argument("ns", "CGMH");
SourceName = Argument("src", "");
RootPath = Argument("root", ".");
TempPath = Argument("temp", ".local\\temp");
NuSpecPath = Argument("nuspec", "source.nuspec");
ToolsPath = Argument("tools", ".local");
ArtifactPath = Argument("output", "");
ExcludeSources = Argument("excl", "").Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries);
ExcludeSourceRegex = Argument("exclr", "").Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries);
IncludeExtensions = Argument("incl", "").Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries);
Targets = Argument("tgts", "").Split(new char[] {','}, StringSplitOptions.RemoveEmptyEntries);

var _tempPath = new DirectoryPath(TempPath);
var _rootPath = new DirectoryPath(RootPath);
var _nuspecPath = new FilePath(NuSpecPath);
var _artifactPath = new DirectoryPath(ArtifactPath);
var _toolsPath = new DirectoryPath(ToolsPath);
_target = Argument("target", "Default");
_configuration = Argument("configuration", "Debug");
_accessibility = Argument("accessibility", "internal");
_version = Argument("newver", "");
_isAzureDevOps = HasArgument("azure");
_nextVersion = HasArgument("nv");

// =======================
//   Load extra variables
// =======================
_isRelease = string.Equals("Release", _configuration, StringComparison.OrdinalIgnoreCase);



// =======================
//   Task Collection
// =======================
Task("Default")
    .Description("default build task")
    .IsDependentOn("PublsihArtifact")
    .Does(() => { });

Task("Debug")
    .Description("debugger task")
    .IsDependentOn("CreatePackage")
    .Does(()=> { });

Task("CreateSource")
    .Description("create source code from project")
    .IsDependentOn("Clean")
    .Does(() => { 
        Information("create source");        
        var targets = new List<string>(Targets.Select(tgt => tgt.ToUpper()));
        if(targets.Count == 0)
            targets.Add("any");
        var paths = targets.ToDictionary(r=>r, r=>new DirectoryPath($"contentFiles/{r.ToLower()}/{SourceName}"));
        var compileds = RecursiveGetFile(_rootPath, "*.cs", DirectionaryPredicate);
        var absoluteRootPath = new DirectoryPath(System.IO.Path.GetFullPath(_rootPath.ToString()));
        Information("find compiled: {0} files", compileds.Count);        
        foreach(var file in compileds) {
            // check if include
            var name = file.GetFilename().FullPath;            
            if(ExcludeSources.Any(r=>string.Equals(name, r)))
                continue;  // if in excludes, continue
            if(ExcludeSourceRegex.Any(r=>System.Text.RegularExpressions.Regex.IsMatch(name, r)))
                continue;  // if in exclude regexes, continue
            
            var relativeFilePath = absoluteRootPath.GetRelativePath(file).AppendExtension(".pp");            
            var writables = targets.ToDictionary(r=>r, r=>true);    
            var ppPaths = paths.ToDictionary(r=>r.Key, r=>r.Value.CombineWithFilePath(relativeFilePath));
            Information("convert file '{0}'", file);
            var lines = FileReadLines(file);
            var contents = ppPaths.ToDictionary(r=>r.Key, r=> new List<string>());
            for(var lineNum= 0; lineNum < lines.Length; lineNum++) {
                var trimLine = lines[lineNum].TrimStart();
                if(trimLine.StartsWith("#if") || trimLine.StartsWith("#elif")) {
                    foreach(var key in writables.Keys)
                        writables[key] = ContainsTarget(trimLine, key);
                    continue;
                }
                else if(trimLine.StartsWith("#else")) {
                    foreach(var key in writables.Keys)
                        writables[key] = !writables[key];
                    continue;
                }
                else if(trimLine.StartsWith("#endif")) {
                    foreach(var key in writables.Keys)
                        writables[key] = true;
                    continue;
                }
                else if(trimLine.StartsWith("[Accessibility")) {
                    var newNextLine = lines[lineNum + 1].Replace("public", _accessibility);
                    lines[lineNum + 1] = newNextLine;
                    continue;
                }
                else if(trimLine.StartsWith("namespace ")) {
                    lines[lineNum] = lines[lineNum].Replace(NameSpacePattern, "$rootnamespace$");
                }

                foreach(var key in writables.Where(r=>r.Value).Select(r=>r.Key)) {
                    contents[key].Add(lines[lineNum]);
                }
            }
            foreach(var ppPath in ppPaths) {
                var filePath = _tempPath.CombineWithFilePath(ppPath.Value);
                var contentLines = contents[ppPath.Key].ToArray();
                EnsureDirectoryExists(filePath.GetDirectory());
                FileWriteLines(filePath, contentLines);
            }
        }

        if(IncludeExtensions.Length > 0) {
            
            foreach(var ext in IncludeExtensions) {
                Information("search included '{0}' files", ext);
                var includes = RecursiveGetFile(_rootPath, "*." + ext, DirectionaryPredicate);
                Information("find '{0}' included: {0} files", includes.Count);
                foreach(var file in includes) {                    
                    var relativeFilePath = absoluteRootPath.GetRelativePath(file);
                    Information("copying '{0}'", relativeFilePath);
                    foreach(var path in paths) {
                        var filePath = path.Value.CombineWithFilePath(relativeFilePath);
                        CopyFile(file, filePath);
                    }
                }
            }
        }
    });

Task("CopyNuspec")
    .Description("copy nuspec file")
    .Does(()=>{
        Information("copy {0} file to {1}", _nuspecPath, _tempPath);
        CopyFileToDirectory(_nuspecPath, _tempPath);
        _nuspecPath = _tempPath.GetFilePath(_nuspecPath.GetFilename());
        Information("nuspec file: {0}", _nuspecPath);
    });
Task("CreatePackage")
    .Description("create final package")
    .IsDependentOn("CreateSource")
    .IsDependentOn("CopyNuspec")
    .Does(() => {         
        var accessibilityText = _accessibility.First().ToString().ToUpper() + _accessibility.Substring(1);
        var id = $"{Id}.{accessibilityText}";
        if(!_isAzureDevOps && string.IsNullOrEmpty(_version)) {
            Information("use local version");
            // get version
            var verFile = RecursiveGetFile(_toolsPath, "release.ver").FirstOrDefault();
            var debugFile = RecursiveGetFile(_toolsPath, "debug.ver").FirstOrDefault();
            if(verFile != null) {                
                _version = FileReadText(verFile);            
                Information("find local release version: {0}", _version);
            }
            else 
                _version = "1.0.0";
            
            if(!_isRelease) {
                Information("use debug configuration");                
                var debugVer = "0";
                if(debugFile != null) {                    
                    debugVer = FileReadText(debugFile);
                    Information("find local debug version: {0}", debugVer);
                }
                if(int.TryParse(debugVer, out int debugVerInt)) {
                    debugVerInt += 1;
                    if(Version.TryParse(_version, out Version ver)) {
                        _version = $"{ver.Major}.{ver.Minor}.{ver.Build}-beta-{debugVerInt}";
                    }          
                    if(_nextVersion) {
                        Information("save debug version: {0}", debugVerInt);
                        FileWriteText(debugFile, debugVerInt.ToString());
                    }
                }
            }
            else if(_nextVersion){
                FileWriteText(debugFile, "0");
                if(Version.TryParse(_version, out Version ver)) {
                    var newVersion = $"{ver.Major}.{ver.Minor}.{ver.Build + 1}.0";
                    Information("save release version: {0}", newVersion);
                    FileWriteText(verFile, newVersion);
                }                
            }

        }
        Information("create package: {0}, version: {1}, output to:{2}", id, _version, _artifactPath);
        NuGetPack(_nuspecPath, new NuGetPackSettings
        {
            Id = id,
            Version = _version,
            OutputDirectory = _artifactPath
        });
    });

Task("NextVersion")
    .Description("move version file to next version")
    .Does(()=> {

    });
Task("CurrentVersion")
    .Description("get current version from files")
    .Does(()=> {

    });
Task("PublsihArtifact")
    .Description("push artifact to online")
    .IsDependentOn("CreatePackage")
    .Does(()=>{        
        var packages = RecursiveGetFile(_artifactPath, "*.nupkg");
        foreach(var package in packages)
        {
            NuGetPush(package, new NuGetPushSettings {
                Source = "http://cghllms1:7081/v3/index.json",
                ApiKey = "cgmh"
            });            
        }
        
    });

Task("Clean")
    .Description("clean task")
    .Does(() => {
        Information("clean artifact path: {0}", _artifactPath);
        CleanDirectory(_artifactPath);
        Information("clean temporary path: {0}", _tempPath);
        CleanDirectory(_tempPath);
     });

// =======================
//   Private Functions
// =======================
IEnumerable<FilePath> RecursiveGetFile(ICakeContext context, DirectoryPath directoryPath,
    string filter, Func<string, bool> predicate, bool recursive)
{
    var directory = context.FileSystem.GetDirectory(context.MakeAbsolute(directoryPath));
    foreach(var file in directory.GetFiles(filter, SearchScope.Current))    
        yield return file.Path;

    if(recursive) {
        var files = directory.GetDirectories("*.*", SearchScope.Current)
                            .Where(dir => predicate(dir.Path.FullPath))
                            .SelectMany(childDirectory => RecursiveGetFile(context, childDirectory.Path, filter, predicate, recursive));    
        foreach(var file in files)    
            yield return file;    
    }
}

List<FilePath> RecursiveGetFile(DirectoryPath directoryPath, string filter, Func<string, bool> predicate = null, bool recursive = true)
{
    if(predicate == null)
        predicate = path => true;
    return RecursiveGetFile(Context, directoryPath, filter, predicate, recursive).ToList();
}
bool DirectionaryPredicate(string path) 
{
    return !path.EndsWith("obj", StringComparison.OrdinalIgnoreCase);
}
string GetTargetNameSymbol(string name) 
{
    return name.Replace('.', '_').ToUpper();
}
bool ContainsTarget(string line, string target) 
{
    line = line.ToUpper();
    target = GetTargetNameSymbol(target);
    var index = line.IndexOf(target);
    if(index == -1)
        return false;
    var inverseChar = line[index - 1];
    if(inverseChar == '!')
        return false;
    return true;
}
// run target    
RunTarget(_target);