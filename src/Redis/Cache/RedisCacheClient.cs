﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Foundatio.Serializer;
using StackExchange.Redis;

namespace Foundatio.Caching {
    public class RedisCacheClient : ICacheClient, IHaveSerializer {
        private readonly ConnectionMultiplexer _connectionMultiplexer;
        private readonly IDatabase _db;
        private readonly ISerializer _serializer;

        public RedisCacheClient(ConnectionMultiplexer connectionMultiplexer, ISerializer serializer = null) {
            _connectionMultiplexer = connectionMultiplexer;
            _db = connectionMultiplexer.GetDatabase();
            _serializer = serializer ?? new JsonNetSerializer();
        }
        
        public async Task<int> RemoveAllAsync(IEnumerable<string> keys = null) {
            if (keys == null || !keys.Any()) {
                var endpoints = _connectionMultiplexer.GetEndPoints(true);
                if (endpoints.Length == 0)
                    return 0;

                foreach (var endpoint in endpoints) {
                    var server = _connectionMultiplexer.GetServer(endpoint);

                    try {
                        await server.FlushDatabaseAsync();
                        continue;
                    } catch (Exception) {}

                    try {
                        var redisKeys = server.Keys().ToArray();
                        if (redisKeys.Length > 0) {
                            await _db.KeyDeleteAsync(redisKeys);
                        }
                    } catch (Exception) {}
                }
            } else {
                var redisKeys = keys.Where(k => !String.IsNullOrEmpty(k)).Select(k => (RedisKey)k).ToArray();
                if (redisKeys.Length > 0) {
                    await _db.KeyDeleteAsync(redisKeys);
                    return redisKeys.Length;
                }
            }

            return 0;
        }

        public Task RemoveByPrefixAsync(string prefix) {
            return _db.ScriptEvaluateAsync("return redis.call('del', unpack(redis.call('keys', ARGV[1])))", null, new[] { (RedisValue)(prefix + "*") });
        }

        public async Task<CacheValue<T>> TryGetAsync<T>(string key) {
            var redisValue = await _db.StringGetAsync(key);
            if (redisValue == RedisValue.Null)
                return CacheValue<T>.Null;

            T value;
            if (typeof(T) == typeof(Int16) || typeof(T) == typeof(Int32) || typeof(T) == typeof(Int64) || typeof(T) == typeof(bool) || typeof(T) == typeof(double))
                value = (T)Convert.ChangeType(redisValue, typeof(T));
            else
                value = _serializer.Deserialize<T>((string)redisValue);

            return new CacheValue<T>(value, true);
        }

        public async Task<IDictionary<string, T>> GetAllAsync<T>(IEnumerable<string> keys) {
            var keyArray = keys.ToArray();
            var values = await _db.StringGetAsync(keyArray.Select(k => (RedisKey)k).ToArray());

            var result = new Dictionary<string, T>();
            for (int i = 0; i < keyArray.Length; i++) {
                T value = _serializer.Deserialize<T>((string)values[i]);
                result.Add(keyArray[i], value);
            }

            return result;
        }

        public async Task<bool> AddAsync<T>(string key, T value, TimeSpan? expiresIn = null) {
            if (expiresIn?.Ticks < 0) {
                await this.RemoveAsync(key);
                return false;
            }

            return await InternalSetAsync(key, value, expiresIn, When.NotExists);
        }

        public Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiresIn = null) {
            return InternalSetAsync(key, value, expiresIn);
        }

        protected Task<bool> InternalSetAsync<T>(string key, T value, TimeSpan? expiresIn = null, When when = When.Always, CommandFlags flags = CommandFlags.None) {
            if (typeof(T) == typeof(Int16))
                return _db.StringSetAsync(key, Convert.ToInt16(value), expiresIn, when, flags);
            if (typeof(T) == typeof(Int32))
                return _db.StringSetAsync(key, Convert.ToInt32(value), expiresIn, when, flags);
            if (typeof(T) == typeof(Int64))
                return _db.StringSetAsync(key, Convert.ToInt64(value), expiresIn, when, flags);
            if (typeof(T) == typeof(bool))
                return _db.StringSetAsync(key, Convert.ToBoolean(value), expiresIn, when, flags);

            var data = _serializer.Serialize(value);
            return _db.StringSetAsync(key, data, expiresIn, when, flags);
        }

        public async Task<int> SetAllAsync<T>(IDictionary<string, T> values, TimeSpan? expiresIn = null) {
            if (values == null)
                return 0;

            await _db.StringSetAsync(values.ToDictionary(v => (RedisKey)v.Key, v => (RedisValue)_serializer.Serialize(v.Value)).ToArray());
            return values.Count;
        }

        public Task<bool> ReplaceAsync<T>(string key, T value, TimeSpan? expiresIn = null) {
            return InternalSetAsync(key, value, expiresIn, When.Exists);
        }

        public async Task<long> IncrementAsync(string key, int amount = 1, TimeSpan? expiresIn = null) {
            if (expiresIn?.Ticks < 0) {
                await this.RemoveAsync(key);
                return -1;
            }
            
            var result = amount >= 0 ? await _db.StringIncrementAsync(key, amount) : await _db.StringDecrementAsync(key, -amount);
            _db.KeyExpire(key, expiresIn);
            return result;
        }

        public Task<TimeSpan?> GetExpirationAsync(string key) {
            return _db.KeyTimeToLiveAsync(key);
        }

        public Task SetExpirationAsync(string key, TimeSpan expiresIn) {
            if (expiresIn.Ticks < 0)
                return this.RemoveAsync(key);

            return _db.KeyExpireAsync(key, expiresIn);
        }

        public void Dispose() {}
        
        ISerializer IHaveSerializer.Serializer {
            get { return _serializer; }
        }
    }
}