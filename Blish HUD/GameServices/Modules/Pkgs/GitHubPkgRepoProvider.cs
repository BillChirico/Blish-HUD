﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Flurl.Http;
using Newtonsoft.Json;

namespace Blish_HUD.Modules.Pkgs {
    public class GitHubPkgRepoProvider : IPkgRepoProvider {

        private static readonly Logger Logger = Logger.GetLogger<GitHubPkgRepoProvider>();

        private const string GITHUB_RELEASES_URI = "/releases";
        private const string ASSET_PACKAGE_NAME  = "packages.gz";

        [Serializable]
        public struct GitHubRelease {

            public string Name { get; set; }

            public GitHubAsset[] Assets { get; set; }

        }

        [Serializable]
        public struct GitHubAsset {

            public string Name { get; set; }

            [JsonProperty("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }

        }

        protected virtual string RepoUrl { get; }

        private static readonly Dictionary<string, PkgManifest[]> _pkgCache = new Dictionary<string, PkgManifest[]>();

        protected GitHubPkgRepoProvider() { /* NOOP */ }

        public GitHubPkgRepoProvider(string repoUrl) {
            this.RepoUrl = repoUrl;
        }

        public async Task<bool> Load(IProgress<string> progress) {
            if (!_pkgCache.ContainsKey(this.RepoUrl)) {
                _pkgCache[this.RepoUrl] = await LoadRepo(progress);
            }

            return true;
        }

        private async Task<PkgManifest[]> LoadRepo(IProgress<string> progress = null) {
            progress?.Report(Strings.GameServices.ModulesService.PkgManagement_Progress_CheckingRepository);
            var releaseResult = await UpdateGitHubReleases();
            
            if (releaseResult.Exception != null) {
                progress?.Report($"{Strings.GameServices.ModulesService.PkgManagement_Progress_FailedToGetReleases}\r\n{releaseResult.Exception.Message}");
                return null;
            }

            progress?.Report(Strings.GameServices.ModulesService.PkgManagement_Progress_GettingModuleList);
            var manifests = await LoadPkgManifests(releaseResult.Releases);

            if (manifests.Exception != null) {
                progress?.Report($"{Strings.GameServices.ModulesService.PkgManagement_Progress_FailedToReadOrParseRepoManifest}\r\n{manifests.Exception.Message}");
                return null;    
            }

            return manifests.PkgManifests;
        }

        private async Task<(GitHubRelease[] Releases, Exception Exception)> UpdateGitHubReleases() {
            try {
                return (await $"{this.RepoUrl}{GITHUB_RELEASES_URI}".WithHeader("User-Agent", "Blish-HUD").GetJsonAsync<GitHubRelease[]>(), null);
            } catch (FlurlHttpException ex) {
                return (new GitHubRelease[0], ex);
            }
        }

        private async Task<(PkgManifest[] PkgManifests, Exception Exception)> LoadPkgManifests(IEnumerable<GitHubRelease> releases) {
            Exception lastException = null;

            foreach (var release in releases) {
                string compressedReleaseUrl = release.Assets.First(asset => asset.Name.Equals(ASSET_PACKAGE_NAME, StringComparison.InvariantCultureIgnoreCase)).BrowserDownloadUrl;

                try {
                    using var compressedRelease = await compressedReleaseUrl.GetStreamAsync();

                    using var gzipStream = new GZipStream(compressedRelease, CompressionMode.Decompress);
                    using var streamReader = new StreamReader(gzipStream);
                    using var jsonTextReader = new JsonTextReader(streamReader);
                    var serializer = new JsonSerializer();

                    return (serializer.Deserialize<PkgManifest[]>(jsonTextReader), null);
                } catch (Exception ex) {
                    Logger.Warn(ex, $"Failed to load release list from '{compressedReleaseUrl}'.");
                    lastException = ex;
                }
            }

            return (null, lastException);
        }

        public IEnumerable<PkgManifest> GetPkgManifests(params Func<PkgManifest, bool>[] filters) {
            return _pkgCache[this.RepoUrl].Where(pkg => filters.All(filter => filter(pkg)));
        }

        public IEnumerable<(string OptionName, Action OptionAction)> GetExtraOptions() {
            yield return ("Force Reload Repository", async () => { await LoadRepo(); });
        }

    }
}
