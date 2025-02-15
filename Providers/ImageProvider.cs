﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.Douban.Providers
{
    public class ImageProvider : BaseProvider, IRemoteImageProvider, IHasOrder
    {
        public string Name => "Douban Emby Image Provider";
        public int Order => 3;
        private readonly IHttpClient _httpClient;

        public ImageProvider(IJsonSerializer jsonSerializer,
            ILogger logger) : base(jsonSerializer, logger)
        {
            // empty
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, LibraryOptions libraryOptions,
            CancellationToken cancellationToken)
        {
            var itemName = GetMovieNameByInfoName(item.Name);
            _logger.LogCallerInfo($"[DOUBAN] GetImages for item: {itemName}");

            var list = new List<RemoteImageInfo>();
            var sid = item.GetProviderId(ProviderID);
            if (string.IsNullOrWhiteSpace(sid))
            {
                var searchResults = await Search<Movie>(itemName, cancellationToken);
                sid = searchResults.FirstOrDefault()?.Id;
                if (string.IsNullOrWhiteSpace(sid))
                {
                    _logger.LogCallerInfo($"[DOUBAN] Got images failed because the sid of \"{itemName}\" is empty!");
                    return list;
                }
            }

            var primaryList = await GetPrimary(sid, item is Movie ? "movie" : "tv", cancellationToken);
            list.AddRange(primaryList);

            // TODO(Libitum): Add backdrop back.
            var backdropList = await GetBackdrop(sid, cancellationToken);
            list.AddRange(backdropList);

            return list;
        }

        public bool Supports(BaseItem item)
        {
            return item is Movie || item is Series;
        }

        public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
        {
            return new List<ImageType>
            {
                ImageType.Primary,
                ImageType.Backdrop
            };
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetPrimary(string sid, string type,
            CancellationToken cancellationToken)
        {
            var list = new List<RemoteImageInfo>();
            var item = await _doubanClient.GetSubject(sid, (MediaType) Enum.Parse(typeof(MediaType), type),
                cancellationToken);
            list.Add(new RemoteImageInfo
            {
                ProviderName = Name,
                Url = item.Pic.Large,
                Type = ImageType.Primary
            });
            return list;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetBackdrop(string sid,
            CancellationToken cancellationToken)

        {
            _logger.Info("IM GetBackdrop url");
            var url = string.Format("https://movie.douban.com/subject/{0}/photos?" +
                                    "type=W&start=0&sortby=size&size=a&subtype=a", sid);

            var response = await _doubanClient.GetAsync(url, cancellationToken);
            var stream = await response.Content.ReadAsStreamAsync();
            string content = new StreamReader(stream).ReadToEnd();

            const String pattern = @"(?s)data-id=""(\d+)"".*?class=""prop"">\n\s*(\d+)x(\d+)";
            Match match = Regex.Match(content, pattern);

            var list = new List<RemoteImageInfo>();
            while (match.Success)
            {
                string data_id = match.Groups[1].Value;
                string width = match.Groups[2].Value;
                string height = match.Groups[3].Value;

                if (float.Parse(width) > float.Parse(height) * 1.3)
                {
                    // Just chose the Backdrop which width is larger than height
                    list.Add(new RemoteImageInfo
                    {
                        ProviderName = Name,
                        Url = string.Format("https://img9.doubanio.com/view/photo/l/public/p{0}.webp", data_id),
                        Type = ImageType.Backdrop,
                    });
                }

                match = match.NextMatch();
            }

            return list;
        }

        Task<HttpResponseInfo> IRemoteImageProvider.GetImageResponse(string url, CancellationToken cancellationToken)
        {
            _logger.Info("IM GetImageResponse url:" + url);
            if (url.IndexOf("Plugins/alifeline_douban/Image", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                url = url.Replace("/emby/Plugins/alifeline_douban/Image?url=", "");
            }

            var res = _httpClient.GetResponse(new HttpRequestOptions
            {
                Url = url
            });
            return res;
        }
    }
}