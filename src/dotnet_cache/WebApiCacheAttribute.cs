using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Caching;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace dotnet_cache
{
    public class WebApiCacheAttribute : ActionFilterAttribute
    {
        /// <summary>
        /// How long cache should stay alive on the server
        /// </summary>
        public Int32 ServerTimeSpan { get; private set; } = 120;

        /// <summary>
        /// How long current response should be valid on the client
        /// </summary>
        public Int32 ClientTimeSpan { get; private set; } = 60;

        /// <summary>
        /// Only allow anonymous calls
        /// </summary>
        public Boolean AnonymousOnly { get; private set; } = false;

        /// <summary>
        /// Cachekey to be affiliated with stored object
        /// </summary>
        public String CacheKey { get; private set; }


        /// <summary>
        /// Object cache store
        /// </summary>
        private static readonly ObjectCache _cache = MemoryCache.Default;

        public WebApiCacheAttribute()
        {

        }

        public WebApiCacheAttribute(int serverTimespan, int clientTimeSpan, bool anonymousOnly)
        {
            this.ServerTimeSpan = serverTimespan;
            this.ClientTimeSpan = clientTimeSpan;
            this.AnonymousOnly = anonymousOnly;
        }

        /// <summary>
        /// Tries to validate if current context actually is cacheable.
        /// Which means it has to be GET method and not only anonymously allowed
        /// </summary>
        /// <param name="context">Current HttpActionContext</param>
        /// <returns>True if current context is cacheable and false if not</returns>
        private Boolean IsCacheAble (HttpActionContext context)
        {
            if (this.ServerTimeSpan > 0 && this.ClientTimeSpan > 0)
            {
                if (this.AnonymousOnly)
                {
                    if (Thread.CurrentPrincipal.Identity.IsAuthenticated)
                        return false;
                }

                if (context.Request.Method == HttpMethod.Get) return true;
            }
            else
            {
                throw new InvalidOperationException("Bad arguments");
            }

            return false;
        }

        /// <summary>
        /// Sets header or client for cached response
        /// </summary>
        /// <returns></returns>
        private CacheControlHeaderValue SetClientCache()
        {
            var cache = new CacheControlHeaderValue();
            cache.MaxAge = TimeSpan.FromSeconds(this.ClientTimeSpan);
            cache.MustRevalidate = true;

            return cache;
        }

        /// <summary>
        /// Tries to fetch any cached context responses in MemoryCache
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        private HttpActionContext FetchCachedContext(HttpActionContext context)
        {
            if (context != null)
            {
                if (this.IsCacheAble(context))
                {
                    this.CacheKey = string.Join(":", new string[]
                    {
                        context.Request.RequestUri.AbsolutePath,
                        context.Request.Headers.Accept.FirstOrDefault().ToString()
                    });

                    if (_cache.Contains(this.CacheKey))
                    {
                        var val = (string)_cache.Get(this.CacheKey);
                        if (val != null)
                        {
                            context.Response = context.Request.CreateResponse();
                            context.Response.Content = new StringContent(val);

                            var contentType = (MediaTypeHeaderValue)_cache.Get($"{this.CacheKey}:response-ct");
                            if (contentType == null)
                                contentType = new MediaTypeHeaderValue(this.CacheKey.Split(':')[1]);

                            context.Response.Content.Headers.ContentType = contentType;
                            context.Response.Headers.CacheControl = SetClientCache();
                            return context;
                        }
                    }
                }

                // Return context as it is if no cached object was found
                return context;
            }
            else
            {
                throw new ArgumentNullException("actionContext");
            }
        }

        /// <summary>
        /// Tries to cache up current context in memorycache
        /// </summary>
        /// <param name="context"></param>
        private void CacheContext(HttpActionExecutedContext context)
        {
            if (!(_cache.Contains(this.CacheKey)))
            {
                var body = context.Response.Content.ReadAsStringAsync().Result;

                _cache.Add(this.CacheKey, body, DateTime.Now.AddSeconds(this.ServerTimeSpan));
                _cache.Add($"{this.CacheKey}:response-ct",
                    context.Response.Content.Headers.ContentType,
                    DateTime.Now.AddSeconds(this.ServerTimeSpan));

                if (this.IsCacheAble(context.ActionContext))
                    context.ActionContext.Response.Headers.CacheControl = this.SetClientCache();
            }
        }

        #region Sync methods
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            var context = this.FetchCachedContext(actionContext);
            base.OnActionExecuting(context);
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            this.CacheContext(actionExecutedContext);
            base.OnActionExecuted(actionExecutedContext);
        }
        #endregion

        #region Async methods
        public override Task OnActionExecutingAsync(HttpActionContext actionContext, CancellationToken cancellationToken)
        {
            var context = this.FetchCachedContext(actionContext);

            return base.OnActionExecutingAsync(context, cancellationToken);
        }

        public override Task OnActionExecutedAsync(HttpActionExecutedContext actionExecutedContext, CancellationToken cancellationToken)
        {
            this.CacheContext(actionExecutedContext);

            return base.OnActionExecutedAsync(actionExecutedContext, cancellationToken);
        }
        #endregion
    }
}
