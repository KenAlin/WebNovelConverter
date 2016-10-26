﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Parser.Html;
using WebNovelConverter.Sources.Models;
using System.IO;
using WebNovelConverter.Sources.Helpers;

namespace WebNovelConverter.Sources
{
    public abstract class WebNovelSource
    {
        public enum Mode
        {
            TableOfContents,
            NextChapterLink
        }

        public abstract List<Mode> AvailableModes { get; }

        protected static readonly ChapterLink[] EmptyLinks = new ChapterLink[0];

        public virtual string BaseUrl { get; protected set; }

        public string SourceName { get; private set; }

        protected readonly HtmlParser Parser = new HtmlParser();

        protected WebNovelSource(string sourceName)
        {
            SourceName = sourceName;
        }

        public virtual Task<IEnumerable<ChapterLink>> GetChapterLinksAsync(string baseUrl, CancellationToken token = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<WebNovelChapter> GetChapterAsync(ChapterLink link, ChapterRetrievalOptions options = default(ChapterRetrievalOptions), CancellationToken token = default(CancellationToken))
        {
            throw new NotImplementedException();
        }

        public virtual Task<WebNovelInfo> GetNovelInfoAsync(string baseUrl, CancellationToken token = default(CancellationToken))
        {
            return Task.FromResult((WebNovelInfo)null);
        }

        protected virtual IEnumerable<ChapterLink> CollectChapterLinks(string baseUrl, IEnumerable<IElement> linkElements,
            Func<IElement, bool> linkFilter = null)
        {
            if (linkFilter != null)
                linkElements = linkElements.Where(linkFilter);

            linkElements = linkElements.Where(p => p.LocalName == "a");

            foreach (IElement e in linkElements)
            {
                if (string.IsNullOrWhiteSpace(e.TextContent) || !e.HasAttribute("href"))
                    continue;

                // Skip social media share links
                if (e.HasAttribute("Class") && e.GetAttribute("Class").Contains("share-icon") && e.GetAttribute("rel") == "nofollow")
                    continue;

                var hrefUri = new Uri(e.GetAttribute("href"), UriKind.RelativeOrAbsolute);
                bool otherDomain = hrefUri.IsAbsoluteUri && hrefUri.Host != new Uri(baseUrl, UriKind.Absolute).Host;
                string url = UrlHelper.ToAbsoluteUrl(baseUrl, e.GetAttribute("href"));

                if (string.IsNullOrEmpty(url))
                    continue;

                ChapterLink link = new ChapterLink
                {
                    Name = e.GetInnerText(),
                    Url = url,
                    Unknown = otherDomain
                };

                yield return link;
            }
        }

        protected async Task<string> GetWebPageAsync(string url, CancellationToken token = default(CancellationToken))
        {
            if( url.StartsWith("file://") && AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.FullName.StartsWith("Microsoft.VisualStudio.QualityTools.UnitTestFramework"))
            )
            {
                return File.ReadAllText(new Uri(url).AbsolutePath);
            }

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WebNovelConverter", "1.0"));
                
                var resp = await client.GetAsync(url, token);
                //resp.EnsureSuccessStatusCode();

                byte[] content = await resp.Content.ReadAsByteArrayAsync();

                return Encoding.UTF8.GetString(content, 0, content.Length);
            }
        }

        public override string ToString()
        {
            return SourceName;
        }
    }
}
