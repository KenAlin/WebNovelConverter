﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using AngleSharp.Dom.Html;
using AngleSharp.Extensions;
using AngleSharp.Parser.Html;
using WebNovelConverter.Extensions;
using WebNovelConverter.Sources.Models;
using WebNovelConverter.Sources.Helpers;

namespace WebNovelConverter.Sources
{
    public class WordPressSource : WebNovelSource
    {
        public override List<Mode> AvailableModes => new List<Mode> { Mode.TableOfContents, Mode.NextChapterLink };

        protected readonly List<string> BloatClasses = new List<string>
        {
            "sharedaddy",
            "share-story-container",
            "code-block",
            "comments-area",
            "pagination"
        };

        protected readonly List<string> PageClasses = new List<string>
        {
            "post-entry",
            "entry-content",
            "post-content",
            "the-content",
            "entry",
            "page-body"
        };

        protected readonly List<string> PostClasses = new List<string>
        {
            "post-entry",
            "entry-content",
            "post-content",
            "postbody",
            "page-body",
            "post",
            "hentry"
        };

        protected readonly List<string> PaginationClasses = new List<string>
        {
            "pagination"
        };

        protected readonly List<string> TitleClasses = new List<string>
        {
            "entry-title",
            "post-title",
            "page-title",
            "title-block"
        };

        protected readonly List<string> NextChapterNames = new List<string>
        {
            "Next Chapter",
            "Next Page",
            "Next"
        };


        protected readonly List<string> NavigationNames = new List<string>
        {
            "Next Chapter",
            "Next",
            "Previous Chapter",
            "Prev",
            "Table of Contents",
            "Index"
        };

        public WordPressSource() : base("WordPress")
        {
        }

        protected WordPressSource(string type) : base(type)
        {
        }

        public override async Task<IEnumerable<ChapterLink>> GetChapterLinksAsync(string baseUrl, CancellationToken token = default(CancellationToken))
        {
            string baseContent = await GetWebPageAsync(baseUrl, token);

            IHtmlDocument doc = await Parser.ParseAsync(baseContent, token);

            var pgElement = doc.DocumentElement.FirstWhereHasClass(PageClasses);

            IElement element = pgElement ?? doc.Descendents<IElement>().FirstOrDefault(p => p.LocalName == "article");
            
            if (element == null)
                return EmptyLinks;

            return CollectChapterLinks(baseUrl, element.Descendents<IElement>());
        }

        public override async Task<WebNovelChapter> GetChapterAsync(ChapterLink link,
            ChapterRetrievalOptions options = default(ChapterRetrievalOptions),
            CancellationToken token = default(CancellationToken))
        {
            string content = await GetWebPageAsync(link.Url, token);
            IHtmlDocument doc = await Parser.ParseAsync(content, token);

            var paged = GetPagedChapterUrls(doc.DocumentElement);

            WebNovelChapter chapter = ParseChapter(doc, link.Url, doc.DocumentElement, token);

            if (chapter == null)
                return null;

            chapter.Url = link.Url;
            chapter.NextChapterUrl = UrlHelper.ToAbsoluteUrl(link.Url, chapter.NextChapterUrl);

            foreach (var page in paged)
            {
                string pageContent = await GetWebPageAsync(page, token);

                IHtmlDocument pageDoc = await Parser.ParseAsync(pageContent, token);

                chapter.Content += ParseChapter(pageDoc, link.Url, pageDoc.DocumentElement, token).Content;
            }

            return chapter;
        }

        private WebNovelChapter ParseChapter(IDocument doc, string baseUrl, IElement rootElement, CancellationToken token = default(CancellationToken))
        {
            IElement articleElement = rootElement.Descendents<IElement>().FirstOrDefault(p => p.LocalName == "article");
            IElement element = rootElement.FirstWhereHasClass(PostClasses) ?? articleElement;

            if (element != null)
                RemoveBloat(element);

            IElement chapterNameElement = rootElement.FirstWhereHasClass(TitleClasses);

            if (element != null && chapterNameElement == null)
            {
                chapterNameElement = (from e in element.Descendents<IElement>()
                                      where e.LocalName == "h1" || e.LocalName == "h2"
                                        || e.LocalName == "h3" || e.LocalName == "h4"
                                      select e).FirstOrDefault();
            }
            else if(chapterNameElement != null)
            {
                IElement chNameLinkElement = (from e in chapterNameElement.Descendents<IElement>()
                                              where e.LocalName == "a"
                                              select e).FirstOrDefault();

                if (chNameLinkElement != null)
                    chapterNameElement = chNameLinkElement;
            }

            IElement nextChapterElement = null;
            if (articleElement != null)
            {
                nextChapterElement = (from e in articleElement.Descendents<IElement>()
                                      where e.LocalName == "a"
                                      let text = e.Text()
                                      let a = NextChapterNames.FirstOrDefault(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                                      where a != null || (e.HasAttribute("rel") && e.GetAttribute("rel") == "next")
                                      let index = NextChapterNames.IndexOf(a)
                                      let o = index >= 0 ? index : int.MaxValue
                                      orderby o
                                      select e).FirstOrDefault();
            }
            if (nextChapterElement == null)
            {
                nextChapterElement = (from e in rootElement.Descendents<IElement>()
                                      where e.LocalName == "a"
                                      let text = e.Text()
                                      let a = NextChapterNames.FirstOrDefault(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                                      where a != null || (e.HasAttribute("rel") && e.GetAttribute("rel") == "next")
                                      let index = NextChapterNames.IndexOf(a)
                                      let o = index >= 0 ? index : int.MaxValue
                                      orderby o
                                      select e).FirstOrDefault();
            }

            WebNovelChapter chapter = new WebNovelChapter();
            if (nextChapterElement != null)
            {
                chapter.NextChapterUrl = nextChapterElement.GetAttribute("href");
            }

            if (element != null)
            {
                chapter.ChapterName = chapterNameElement?.Text()?.Trim();

                RemoveNavigation(element);
                RemoveScriptStyleElements(element);
                RemoveTitles(element);
                chapter.Content = new ContentCleanup(baseUrl).Execute(doc, element);
            }
            else
            {
                chapter.Content = "No Content";
            }

            return chapter;
        }

        /// <summary>
        /// Titles are often inside the chapter-content part
        /// </summary>
        /// <param name="element"></param>
        private void RemoveTitles(IElement element)
        {
            var titleEl = element.QuerySelectorAll("h1,h2,h3,h4,h5")
                .Where(x => TitleClasses.Any(t => x.ClassName != null && x.ClassName.Contains(t)))
                .FirstOrDefault();
            titleEl?.Remove();
        }

        protected virtual IEnumerable<string> GetPagedChapterUrls(IElement rootElement)
        {
            var pagElements = rootElement.FirstWhereHasClass(PaginationClasses, e => e.LocalName == "div")
                ?.Descendents<IElement>();

            pagElements = pagElements?.Where(p => p.LocalName == "a");

            if (pagElements == null)
                return new List<string>();

            return pagElements.Select(p => p.GetAttribute("href"));
        }

        protected virtual void RemoveBloat(IElement element)
        {
            var shareElements = element.WhereHasClass(BloatClasses);

            foreach (IElement e in shareElements)
            {
                e.Remove();
            }
        }

        protected virtual void RemoveNavigation(IElement element)
        {
            var navElements = from e in element.Descendents<IElement>()
                              where e.LocalName == "a"
                              let text = e.Text()
                              where NavigationNames.Any(p => text.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                              select e;

            foreach (IElement e in navElements.ToList())
            {
                e.Remove();
            }
        }

        protected virtual void RemoveScriptStyleElements(IElement element)
        {
            var elements = from e in element.Descendents<IElement>()
                           where e.LocalName == "script" || e.LocalName == "style"
                           select e;

            foreach (IElement e in elements.ToList())
            {
                e.Remove();
            }
        }

        public override async Task<WebNovelInfo> GetNovelInfoAsync(string baseUrl, CancellationToken token = default(CancellationToken))
        {
            string baseContent = await GetWebPageAsync(baseUrl, token);

            IHtmlDocument doc = await Parser.ParseAsync(baseContent, token);

            var title = (
                doc.QuerySelector(".entry-header .entry-title")
                ??
                doc.QuerySelector("h1.entry-title")
                ??
                doc.QuerySelector(".entry-header h1.page-title")
                )?.GetInnerText();

            return new WebNovelInfo
            {
                Title = title
            };
        }
    }
}
