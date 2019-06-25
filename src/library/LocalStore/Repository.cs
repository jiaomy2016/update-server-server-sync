﻿using Microsoft.UpdateServices.Metadata;
using Microsoft.UpdateServices.Metadata.Content;
using Microsoft.UpdateServices.Query;
using Microsoft.UpdateServices.WebServices.ServerSync;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace Microsoft.UpdateServices.LocalCache
{
    public class Repository
    {
        /// <summary>
        /// The file containing authentication data
        /// </summary>
        private const string AuthenticationFileName = "wu-server-auth.json";
        public string AuthenticationFilePath => Path.Combine(LocalPath, AuthenticationFileName);

        /// <summary>
        /// The file containing server configuration data
        /// </summary>
        private const string ConfigurationFileName = "wu-server-config.json";
        public string ConfigurationFilePath => Path.Combine(LocalPath, ConfigurationFileName);

        /// <summary>
        /// The file containing categories data
        /// </summary>
        const string CategoriesFileName = "wu-server-categories.json";
        public string CategoriesFilePath => Path.Combine(LocalPath, CategoriesFileName);

        /// <summary>
        /// The file containing updates metadata
        /// </summary>
        const string UpdatesFileName = "wu-server-updates.json";
        public string UpdatesFilePath => Path.Combine(LocalPath, UpdatesFileName);

        /// <summary>
        /// Root content directory name
        /// </summary>
        private const string ContentDirectoryName = "content";
        public string ContentDirectoryPath => Path.Combine(LocalPath, ContentDirectoryName);

        /// <summary>
        /// Root XML metadata directory name
        /// </summary>
        private const string XmlMetadataDirectoryName = "xml-data";
        public string XmlMetadataDirectoryPath => Path.Combine(LocalPath, XmlMetadataDirectoryName);

        public CategoriesCache Categories{ get; private set; }

        public UpdatesCache Updates { get; private set; }

        public event EventHandler<RepoOperationProgress> RepositoryOperationProgress;

        /// <summary>
        /// Given an update file, returns the path to the file in local store
        /// </summary>
        /// <param name="updateFile">The file to get the path for</param>
        /// <returns>Fully qualified path to the file. The path might not exist.</returns>
        public string GetUpdateFilePath(UpdateFile updateFile)
        {
            if (updateFile.Digests.Count == 0)
            {
                throw new Exception("Cannot determine file path for update with no digest");
            }

            byte[] hashBytes = Convert.FromBase64String(updateFile.Digests[0].DigestBase64);
            var contentSubDirectory = string.Format("{0:X}", hashBytes.Last());

            return Path.Combine(LocalPath, ContentDirectoryName, contentSubDirectory, updateFile.Digests[0].DigestBase64, updateFile.FileName);
        }

        /// <summary>
        /// Given an update, returns the path to its XML file in the store.
        /// </summary>
        /// <param name="updateFile">The update to get the path for</param>
        /// <returns>Fully qualified path to the file. The path might not exist.</returns>
        public string GetUpdateXmlPath(MicrosoftUpdate update)
        {
            var contentSubDirectory = update.Identity.Raw.UpdateID.ToByteArray().Last().ToString();
            return Path.Combine(LocalPath, XmlMetadataDirectoryName, contentSubDirectory, update.Identity.ToString() + ".xml");
        }

        public string LocalPath = null;

        private Repository() { }

        public enum RepositoryOpenMode
        {
            OpenExisting,
            CreateIfDoesNotExist
        }

        /// <summary>
        /// Loads a repository from a directory (or creates a new one if no repo exists in the directory)
        /// </summary>
        /// <param name="repoDirectory">The path to open or create the repository in</param>
        /// <returns>A new file system backed repository</returns>
        public static Repository FromDirectory(string repoDirectory, RepositoryOpenMode openMode = RepositoryOpenMode.OpenExisting)
        {
            var newRepository = new Repository() { LocalPath = repoDirectory };

            if (!Directory.Exists(repoDirectory))
            {
                if (openMode == RepositoryOpenMode.OpenExisting)
                {
                    return null;
                }

                Directory.CreateDirectory(repoDirectory);
            }

            newRepository.Updates = UpdatesCache.FromRepository(newRepository);
            newRepository.Updates.ParentRepository = newRepository;

            newRepository.Categories = CategoriesCache.FromRepository(newRepository);
            newRepository.Categories.ParentRepository = newRepository;

            return newRepository;
        }

        /// <summary>
        /// Write all pending changes to disk
        /// </summary>
        public void Commit()
        {

        }

        /// <summary>
        /// Delete the repository stored at the specified path
        /// </summary>
        /// <param name="path">The path that contains the repository to delete</param>
        public static void Delete(string path)
        {
            // Create an empty repository, only initialize the path that will be used to delete
            // the various files that make up the repository
            var repoToDelete = new Repository() { LocalPath = path };
            CategoriesCache.Delete(repoToDelete);
            UpdatesCache.Delete(repoToDelete);

            repoToDelete.Delete();
        }

        /// <summary>
        /// Deletes the repository from disk and clears all cached data from memory
        /// </summary>
        public void Delete()
        {
            var filesToDelete = new string[] { AuthenticationFilePath, ConfigurationFilePath };
            foreach(var fileToDelete in filesToDelete)
            {
                if (File.Exists(fileToDelete))
                {
                    File.Delete(fileToDelete);
                }
            }

            var directoriesToDelete = new string[] { XmlMetadataDirectoryPath, ContentDirectoryPath };
            foreach(var dirToDelete in directoriesToDelete)
            {
                if (Directory.Exists(dirToDelete))
                {
                    Directory.Delete(dirToDelete, true);
                }
            }

            Categories?.Delete();
            Updates?.Delete();
        }

        /// <summary>
        /// Adds new updates or categories to the store.
        /// </summary>
        /// <param name="queryResult">The query result to merge with the store.</param>
        public void MergeQueryResult(QueryResult queryResult)
        {
            if (queryResult.Filter.IsCategoriesQuery)
            {
                Categories.MergeQueryResult(queryResult);
            }
            else
            {
                Updates.MergeQueryResult(queryResult, Categories);
            }
        }

        /// <summary>
        /// Get a cached access token from disk
        /// </summary>
        /// <returns>Cached token if it exists, null otherwise</returns>
        public ServiceAccessToken GetAccessToken()
        {
            if (File.Exists(AuthenticationFilePath))
            {
                return JsonConvert.DeserializeObject<ServiceAccessToken>(File.ReadAllText(AuthenticationFilePath));
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Caches an access token for future use. Overwrites the previusly cached token.
        /// </summary>
        /// <param name="accessToken">The token to cache.</param>
        public void CacheAccessToken(ServiceAccessToken accessToken)
        {
            File.WriteAllText(AuthenticationFilePath, JsonConvert.SerializeObject(accessToken));
        }

        /// <summary>
        /// Gets the cached service configuration from disk
        /// </summary>
        /// <returns>Service configuration is it exists, null otherwise</returns>
        public ServerSyncConfigData GetServiceConfiguration()
        {
            if (File.Exists(ConfigurationFilePath))
            {
                return JsonConvert.DeserializeObject<ServerSyncConfigData>(File.ReadAllText(ConfigurationFilePath));
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Cacheds service configuration to disk. Overwrites the previously cached configuration.
        /// </summary>
        /// <param name="serverConfig">The configuration to cache</param>
        public void CacheServiceConfiguration(ServerSyncConfigData serverConfig)
        {
            File.WriteAllText(ConfigurationFilePath, JsonConvert.SerializeObject(serverConfig));
        }

        public enum ExportFormat
        {
            WSUS_2016,
        }

        public void Export(List<MicrosoftUpdate> updatesToExport, string exportFilePath, ExportFormat format)
        {
            if (format == ExportFormat.WSUS_2016)
            {
                var configData = GetServiceConfiguration();
                if (configData == null)
                {
                    throw new Exception("Missing configuration data");
                }

                var exporter = new WsusExport(this);
                exporter.ExportProgress += Exporter_ExportProgress;
                exporter.Export(updatesToExport, exportFilePath);
            }
        }

        private void Exporter_ExportProgress(object sender, RepoOperationProgress e)
        {
            RepositoryOperationProgress?.Invoke(this, e);
        }
    }
}
