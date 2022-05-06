//using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Drive.v3.Data;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using System.Collections.Generic;
using System.Linq;

namespace VSSystem.ThirdParty.Google.Drive
{
    public class GGClient
    {
        string _credentialFilePath;
        System.IO.DirectoryInfo _workingFolder;
        static UserCredential _credential;
        readonly string[] _scopes;
        readonly static char[] _splitPathChars = new char[] { '/', '\\' };
        public GGClient(string credentialFilePath, string workingFolderPath = "")
        {
            _scopes = new string[] { DriveService.Scope.DriveFile };
            _credentialFilePath = credentialFilePath;
            if (string.IsNullOrWhiteSpace(workingFolderPath))
            {
                workingFolderPath = System.IO.Directory.GetCurrentDirectory();
            }
            _workingFolder = new System.IO.DirectoryInfo(workingFolderPath.Replace("\\", "/"));
        }
        async Task _Init()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_credentialFilePath))
                {
                    System.IO.FileInfo credentialFile = new System.IO.FileInfo(_credentialFilePath);
                    if (credentialFile.Exists)
                    {
                        FileDataStore fileStore = new FileDataStore(_workingFolder.FullName + "/token.json", false);
                        GoogleClientSecrets secretsObj = null;

                        using (var stream = credentialFile.OpenRead())
                        {
                            secretsObj = await GoogleClientSecrets.FromStreamAsync(stream, CancellationToken.None);
                            stream.Close();
                            stream.Dispose();
                        }
                        _credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                                                secretsObj.Secrets,
                                                _scopes,
                                                "user",
                                                CancellationToken.None, fileStore);
                    }
                }

            }
            catch { }
        }
        async Task<string> _CreateFolder(string[] splitPaths)
        {
            string result = string.Empty;
            try
            {
                if (splitPaths?.Length > 0)
                {
                    if (_credential == null)
                    {
                        await _Init();
                    }
                    if (_credential != null)
                    {
                        var service = new DriveService(new BaseClientService.Initializer()
                        {
                            HttpClientInitializer = _credential,
                        });

                        string parentID = string.Empty;

                        foreach (var sPath in splitPaths)
                        {
                            try
                            {
                                List<string> qPars = new List<string>()
                                {
                                    "mimeType = 'application/vnd.google-apps.folder'",
                                    "trashed=false"
                                };
                                if (!string.IsNullOrWhiteSpace(parentID))
                                {
                                    qPars.Add($"'{parentID}' in parents");
                                }
                                qPars.Add($"name = '{sPath}'");
                                FilesResource.ListRequest folderRequest = service.Files.List();
                                folderRequest.Fields = "nextPageToken, files(id, name, size, version, trashed, createdTime)";
                                folderRequest.Q = string.Join(" and ", qPars);
                                var lFolderObjs = await folderRequest.ExecuteAsync();

                                var resFolderObj = lFolderObjs.Files?.FirstOrDefault();
                                if (resFolderObj == null)
                                {
                                    var folderObj = new File();
                                    if (!string.IsNullOrWhiteSpace(parentID))
                                    {
                                        folderObj.Parents = new List<string>() { parentID };
                                    }
                                    folderObj.Name = sPath;
                                    folderObj.MimeType = "application/vnd.google-apps.folder";

                                    resFolderObj = service.Files.Create(folderObj).Execute();

                                }
                                parentID = resFolderObj.Id;
                            }
                            catch { }
                        }
                        result = parentID;
                    }
                }
            }
            catch { }
            return result;
        }
        public Task<string> CreateFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.FromResult<string>(string.Empty);
            }
            string[] splitPaths = path.Split(_splitPathChars, System.StringSplitOptions.RemoveEmptyEntries);
            return _CreateFolder(splitPaths);
        }

        public async Task<string> UploadFile(string localFilePath, string uploadPath)
        {
            string result = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(localFilePath))
                {
                    return result;
                }
                if (string.IsNullOrWhiteSpace(uploadPath))
                {
                    return result;
                }
                System.IO.FileInfo localFile = new System.IO.FileInfo(localFilePath);

                if (localFile?.Exists ?? false)
                {
                    var uploadFileName = System.IO.Path.GetFileName(uploadPath);
                    int idxFileName = uploadPath.LastIndexOf(uploadFileName, System.StringComparison.InvariantCultureIgnoreCase);
                    string uploadFolderPath = uploadPath.Substring(0, idxFileName);
                    var folderID = await CreateFolder(uploadFolderPath);

                    if (_credential == null)
                    {
                        await _Init();
                    }
                    if (_credential != null)
                    {
                        var service = new DriveService(new BaseClientService.Initializer()
                        {
                            HttpClientInitializer = _credential,
                        });


                        File ggFile = new File();
                        ggFile.Name = uploadFileName;
                        ggFile.Parents = new List<string>() { folderID };
                        using (var fs = localFile.Open(System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                        {
                            var uploadRequest = service.Files.Create(ggFile, fs, "");
                            uploadRequest.Fields = "id";
                            var uploadResponse = uploadRequest.Upload();

                            fs.Close();
                            fs.Dispose();

                            var resFile = uploadRequest.ResponseBody;
                            result = resFile.Id;
                        }


                    }



                }
            }
            catch { }
            return result;
        }

        public async Task<bool> DownloadFile(string fileID, string localFilePath, bool overwrite = false)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(localFilePath))
                {
                    return false;
                }
                if (string.IsNullOrWhiteSpace(fileID))
                {
                    return false;
                }
                if (_credential == null)
                {
                    await _Init();
                }
                if (_credential != null)
                {
                    var service = new DriveService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = _credential,
                    });
                    var request = service.Files.Get(fileID);
                    System.IO.FileInfo localFile = new System.IO.FileInfo(localFilePath);
                    if (!localFile.Directory.Exists)
                    {
                        localFile.Directory.Create();
                    }
                    if (localFile.Exists)
                    {
                        if (overwrite)
                        {
                            localFile.Delete();
                        }
                        else
                        {
                            return false;
                        }
                    }

                    using (var fs = localFile.Open(System.IO.FileMode.OpenOrCreate, System.IO.FileAccess.Write, System.IO.FileShare.Read))
                    {
                        await request.DownloadAsync(fs);
                        fs.Close();
                        fs.Dispose();
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }

        public async Task<string> SetPermission(string fileID)
        {
            string result = string.Empty;
            try
            {
                Permission permissionObj = new Permission();
                permissionObj.Type = "anyone";
                permissionObj.Role = "reader";
                permissionObj.AllowFileDiscovery = true;

                if (_credential == null)
                {
                    await _Init();
                }
                if (_credential != null)
                {
                    var service = new DriveService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = _credential,
                    });

                    var resPermissionObj = await service.Permissions.Create(permissionObj, fileID).ExecuteAsync();
                    result = resPermissionObj.Id;
                }
            }
            catch { }
            return result;
        }
        public async Task<string> GetShareFileLink(string fileID)
        {
            string result = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(fileID))
                {
                    return result;
                }
                if (_credential == null)
                {
                    await _Init();
                }
                if (_credential != null)
                {
                    var service = new DriveService(new BaseClientService.Initializer()
                    {
                        HttpClientInitializer = _credential,
                    });
                    var request = service.Files.Get(fileID);
                    request.Fields = "*";
                    var response = request.Execute();
                    result = response.WebViewLink;
                }
            }
            catch { }
            return result;
        }

    }
}

