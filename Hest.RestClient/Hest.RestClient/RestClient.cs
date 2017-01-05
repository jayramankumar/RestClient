﻿using NLog;
using Polly;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;

namespace IN.ServiceLayer.BPM.Repositories
{
    public class RestClient : IRestClient
    {
        private ILogger logger;

        public Policy DefaultPolicy
        {
            get
            {
                return Policy
                    .Handle<WebException>()
                    .Or<HttpResponseException>(e => !HttpStatusCodesThatShouldNotBeRetried.Contains(e.Response.StatusCode))
                    .WaitAndRetryAsync(
                        retryCount: 4,
                        sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (exception, timespan, context) =>
                        {
                            logger.Error(exception, "Retrying HTTP request after {0} ms", timespan.TotalMilliseconds);
                        }
                    );
            }
        }

        public HttpClient Client;
        public Policy Policy { get; set; }
        public List<HttpStatusCode> HttpStatusCodesWorthRetrying { get; set; } = new List<HttpStatusCode> {
            HttpStatusCode.RequestTimeout,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.BadGateway,
            HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.GatewayTimeout
        };

        public List<HttpStatusCode> HttpStatusCodesThatShouldNotBeRetried { get; set; } = new List<HttpStatusCode>
        {
            HttpStatusCode.BadRequest,
            HttpStatusCode.NotFound,
            HttpStatusCode.Unauthorized
        };

        public RestClient(ILogger logger)
        {
            this.logger = logger;
            initializeClient();
        }

        public RestClient(ILogger logger, Policy policy)
        {
            this.logger = logger;
            Policy = policy;
            initializeClient();
        }

        private void initializeClient()
        {
            Client = new HttpClient(new HttpClientHandler { Credentials = CredentialCache.DefaultNetworkCredentials });
            Client.DefaultRequestHeaders.Accept.Clear();
            Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public void EnableDefaultPolicy()
        {
            Policy = DefaultPolicy;
        }

        public async Task<TResult> GetAsync<TResult>(string url, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            var response = await ExecuteAsync(() => GetAsync(requestUri));
            if (response.StatusCode == HttpStatusCode.NotFound)
                return default(TResult);
            return await ReadAsAsync<TResult>(response);
        }

        public TResult Get<TResult>(string url, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            HttpResponseMessage response = null;
            response = Execute(() => GetAsync(requestUri)).Result;
            return ReadAs<TResult>(response);
        }

        public async Task<TResult> PostAsync<TResult>(string url, object body, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            return await PostAsync<TResult>(requestUri, body);
        }

        public async Task<TResult> PostAsync<TResult>(string url, object body)
        {
            var response = await ExecuteAsync(() => PostAsync(url, body));
            return await ReadAsAsync<TResult>(response);
        }

        public TResult Post<TResult>(string url, object body, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            return Post<TResult>(requestUri, body);
        }

        public TResult Post<TResult>(string url, object body)
        {
            var response = Execute(() => PostAsync(url, body)).Result;
            return ReadAs<TResult>(response);
        }

        public TResult Delete<TResult>(string url, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            var response = Execute(() => DeleteAsync(requestUri)).Result;
            return ReadAs<TResult>(response);
        }

        public async Task<TResult> DeleteAsync<TResult>(string url, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            var response = await ExecuteAsync(() => DeleteAsync(requestUri));
            return await ReadAsAsync<TResult>(response);
        }

        public async Task<TResult> PutAsync<TResult>(string url, object body, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            var response = await ExecuteAsync(() => PutAsync(requestUri, body));
            return await ReadAsAsync<TResult>(response);
        }

        public TResult Put<TResult>(string url, object body, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            var response = Execute(() => PutAsync(requestUri, body)).Result;
            return ReadAs<TResult>(response);
        }

        public async Task<Stream> GetStreamAsync(string url, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            return await Client.GetStreamAsync(requestUri);
        }

        public Stream GetStream(string url, params string[] parameters)
        {
            var requestUri = string.Format(url, parameters);
            return Client.GetStreamAsync(requestUri).Result;
        }

        public void Dispose()
        {
            Client.Dispose();
            GC.SuppressFinalize(this);
        }

        private static async Task<TResult> ReadAsAsync<TResult>(HttpResponseMessage response)
        {
            var result = default(TResult);
            if (response.IsSuccessStatusCode)
            {
                result = await response.Content.ReadAsAsync<TResult>();
            }
            return result;
        }

        private static TResult ReadAs<TResult>(HttpResponseMessage response)
        {
            return ReadAsAsync<TResult>(response).Result;
        }

        private async Task<HttpResponseMessage> GetAsync(string requestUri)
        {
            var response = await Client.GetAsync(requestUri).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return response;
            response.EnsureSuccessStatusCode();
            return response;
        }

        private async Task<HttpResponseMessage> PostAsync(string requestUri, object body)
        {
            var response = await Client.PostAsJsonAsync(requestUri, body).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response;
        }

        private async Task<HttpResponseMessage> DeleteAsync(string requestUri)
        {
            var response = await Client.DeleteAsync(requestUri);
            response.EnsureSuccessStatusCode();
            return response;
        }

        private async Task<HttpResponseMessage> PutAsync(string requestUri, object body)
        {
            var response = await Client.PutAsJsonAsync(requestUri, body);
            response.EnsureSuccessStatusCode();
            return response;
        }

        private async Task<HttpResponseMessage> ExecuteAsync(Func<Task<HttpResponseMessage>> function)
        {
            if (Policy == null)
                return await function?.Invoke();
            else
                return await Policy.ExecuteAsync(() => function?.Invoke());
        }

        private Task<HttpResponseMessage> Execute(Func<Task<HttpResponseMessage>> function)
        {
            if (Policy == null)
                return function?.Invoke();
            else
                return Policy.Execute(() => function?.Invoke());
        }
    }
}