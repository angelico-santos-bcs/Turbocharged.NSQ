﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Turbocharged.NSQ
{
    /// <summary>
    /// A client for querying an instance of nsqlookupd.
    /// </summary>
    public class NsqLookup
    {
        readonly WebClient _webClient = new WebClient();
        readonly SemaphoreSlim _webClientLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Creates a new instance of <c>NsqLookup</c>.
        /// </summary>
        /// <param name="host">The host name or IP address of the nsqlookupd instance.</param>
        /// <param name="port">The HTTP port of the nsqlookupd instance.</param>
        public NsqLookup(string host, int port)
        {
            _webClient.BaseAddress = new UriBuilder()
            {
                Scheme = "http",
                Host = host,
                Port = port,
            }.ToString();
        }

        /// <summary>
        /// Looks up the nsqd instances which are producing a topic.
        /// </summary>
        public Task<List<NsqAddress>> LookupAsync(Topic topic)
        {
            return RequestListAsync("/lookup?topic=" + topic, response =>
            {
                return
                    ((JArray)response["data"]["producers"])
                    .Select(producer => new NsqAddress(
                            (string)producer["broadcast_address"],
                            (string)producer["hostname"],
                            (int)producer["tcp_port"],
                            (int)producer["http_port"]))
                    .ToList();
            });
        }

        /// <summary>
        /// Queries the list of topics known to this nsqlookupd instance.
        /// </summary>
        public Task<List<Topic>> TopicsAsync()
        {
            return RequestListAsync("/topics", response =>
            {
                return response["data"]["topics"]
                    .Select(t => new Topic((string)t))
                    .ToList();
            });
        }

        /// <summary>
        /// Queries the channels known to this nsqlookupd instance.
        /// </summary>
        /// <param name="topic">The topic to query.</param>
        public Task<List<Channel>> ChannelsAsync(Topic topic)
        {
            return RequestListAsync("/channels?topic=" + topic, response =>
            {
                return response["data"]["channels"]
                    .Select(t => new Channel((string)t))
                    .ToList();
            });
        }

        /// <summary>
        /// Queries the nsqd nodes known to this nsqlookupd instance.
        /// </summary>
        public Task<List<NsqAddress>> NodesAsync()
        {
            return RequestListAsync("/nodes", response =>
            {
                return
                    ((JArray)response["data"]["producers"])
                    .Select(producer => new NsqAddress(
                            (string)producer["broadcast_address"],
                            (string)producer["hostname"],
                            (int)producer["tcp_port"],
                            (int)producer["http_port"]))
                    .ToList();
            });
        }

        /// <summary>
        /// Deletes a topic.
        /// </summary>
        public Task DeleteTopicAsync(Topic topic)
        {
            return RequestAsync("/delete_topic?topic=" + topic, _ => true);
        }

        /// <summary>
        /// Deletes a channel.
        /// </summary>
        public Task DeleteChannelAsync(Topic topic, Channel channel)
        {
            var url = "/delete_channel?topic=" + topic + "&channel=" + channel;
            return RequestAsync(url, _ => true);
        }

        /// <summary>
        /// Tombstones a topic for an nsqd instance.
        /// </summary>
        public Task TombstoneTopicProducerAsync(Topic topic, NsqAddress producer)
        {
            var url = string.Format("/tombstone_topic_producer?topic={0}&node={1}:{2}", topic, producer.BroadcastAddress, producer.HttpPort);
            return RequestAsync(url, _ => true);
        }

        /// <summary>
        /// Queries the version of the nsqlookupd instance.
        /// </summary>
        public Task<Version> VersionAsync()
        {
            return RequestAsync("/info", response =>
            {
                var version = (string)response["data"]["version"];
                return new Version(version);
            });
        }

        /// <summary>
        /// Queries the nsqlookupd instance for liveliness.
        /// </summary>
        /// <returns>True if nsqlookupd returns "OK".</returns>
        public Task<bool> PingAsync()
        {
            return RequestAsync("/ping", response =>
            {
                return (string)response == "OK";
            });
        }

        async Task<List<T>> RequestListAsync<T>(string url, Func<JObject, List<T>> handler)
        {
            var result = await RequestAsync(url, handler).ConfigureAwait(false);
            return result ?? new List<T>();
        }

        async Task<T> RequestAsync<T>(string url, Func<JObject, T> handler)
        {
            await _webClientLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string data = await _webClient.DownloadStringTaskAsync(url).ConfigureAwait(false);
                var response = JObject.Parse(data);
                if (response["data"] == null)
                {
                    return default(T);
                }

                return handler(response);
            }
            catch (WebException)
            {
                return default(T);
            }
            finally
            {
                _webClientLock.Release();
            }
        }
    }
}
