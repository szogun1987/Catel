// --------------------------------------------------------------------------------------------------------------------
// <copyright file="CacheStorage.cs" company="Catel development team">
//   Copyright (c) 2008 - 2013 Catel development team. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace Catel.Caching
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Threading;
    using Policies;

    /// <summary>
    /// The cache storage.
    /// </summary>
    /// <typeparam name="TKey">The key type.</typeparam>
    /// <typeparam name="TValue">The value type.</typeparam>
    public class CacheStorage<TKey, TValue> : ICacheStorage<TKey, TValue>
    {
        #region Fields
        private readonly Func<ExpirationPolicy> _defaultExpirationPolicyInitCode;

        /// <summary>
        /// Determines whether the cache storage can store null values.
        /// </summary>
        private readonly bool _storeNullValues;

        /// <summary>
        /// The dictionary.
        /// </summary>
        private readonly Dictionary<TKey, CacheStorageValueInfo<TValue>> _dictionary;

        /// <summary>
        /// The reader/writer lock.
        /// </summary>
        private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();

        /// <summary>
        /// The timer that is being executed to invalidate the cache.
        /// </summary>
        private readonly Timer _timer;

        /// <summary>
        /// Determines whether the cache storage can check for expired items.
        /// </summary>
        private bool _checkForExpiredItems;
        #endregion

        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="CacheStorage{TKey,TValue}" /> class.
        /// </summary>
        /// <param name="defaultExpirationPolicyInitCode">
        ///     The default expiration policy initialization code.
        /// </param>
        /// <param name="storeNullValues">
        ///     Allow store null values on the cache.
        /// </param>
        public CacheStorage(Func<ExpirationPolicy> defaultExpirationPolicyInitCode = null, bool storeNullValues = false)
        {
            _dictionary = new Dictionary<TKey, CacheStorageValueInfo<TValue>>();
            _storeNullValues = storeNullValues;
            _defaultExpirationPolicyInitCode = defaultExpirationPolicyInitCode;

            _timer = new Timer(OnTimerElapsed, null, 1000, 1000);
        }
        #endregion

        #region ICacheStorage<TKey,TValue> Members
        /// <summary>
        /// Gets the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The value associated with the specified key, or default value for the type of the value if the key do not exists.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public TValue this[TKey key]
        {
            get { return Get(key); }
        }

        /// <summary>
        /// Gets the keys so it is possible to enumerate the cache.
        /// </summary>
        /// <value>The keys.</value>
        public IEnumerable<TKey> Keys
        {
            get
            {
                _lock.EnterReadLock();

                try
                {
                    return _dictionary.Keys;
                }
                finally
                {
                    _lock.ExitReadLock();
                }
            }
        }

        /// <summary>
        /// Gets the value associated with the specified key
        /// </summary>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>The value associated with the specified key, or default value for the type of the value if the key do not exists.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public TValue Get(TKey key)
        {
            Argument.IsNotNull("key", key);

            CacheStorageValueInfo<TValue> valueInfo;

            _lock.EnterReadLock();

            try
            {
                _dictionary.TryGetValue(key, out valueInfo);
            }
            finally
            {
                _lock.ExitReadLock();
            }

            return (valueInfo != null) ? valueInfo.Value : default(TValue);
        }

        /// <summary>
        /// Determines whether the cache contains a value associated with the specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns><c>true</c> if the cache contains an element with the specified key; otherwise, <c>false</c>.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public bool Contains(TKey key)
        {
            Argument.IsNotNull("key", key);

            _lock.EnterReadLock();

            try
            {
                return _dictionary.ContainsKey(key);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="code">The deferred initialization code of the value.</param>
        /// <param name="expirationPolicy">The expiration policy.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <returns>The instance initialized by the <paramref name="code" />.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="key" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="code" /> is <c>null</c>.</exception>
        [SuppressMessage("StyleCop.CSharp.SpacingRules", "SA1027:TabsMustNotBeUsed", Justification = "Reviewed. Suppression is OK here.")]
        public TValue GetFromCacheOrFetch(TKey key, Func<TValue> code, ExpirationPolicy expirationPolicy, bool @override = false)
        {
            Argument.IsNotNull("key", key);
            Argument.IsNotNull("code", code);

            TValue value;

            _lock.EnterWriteLock();

            try
            {
                bool containsKey = _dictionary.ContainsKey(key);
                if (!containsKey || @override)
                {
                    value = code.Invoke();
                    if (!ReferenceEquals(value, null) || _storeNullValues)
                    {
                        if (expirationPolicy == null && _defaultExpirationPolicyInitCode != null)
                        {
                            expirationPolicy = _defaultExpirationPolicyInitCode.Invoke();
                        }

                        var valueInfo = new CacheStorageValueInfo<TValue>(value, expirationPolicy);

                        //_lock.EnterWriteLock();

                        //try
                        //{
                            _dictionary[key] = valueInfo;
                        //}
                        //finally
                        //{
                        //    _lock.ExitWriteLock();
                        //}

                        if (valueInfo.CanExpire)
                        {
                            _checkForExpiredItems = true;
                        }
                    }
                }
                else
                {
                    value = _dictionary[key].Value;
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }

            return value;
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="code">The deferred initialization code of the value.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <param name="expiration">The timespan in which the cache item should expire when added.</param>
        /// <returns>The instance initialized by the <paramref name="code" />.</returns>
        /// <exception cref="ArgumentNullException">If <paramref name="key" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">If <paramref name="code" /> is <c>null</c>.</exception>
        public TValue GetFromCacheOrFetch(TKey key, Func<TValue> code, bool @override = false, TimeSpan expiration = default(TimeSpan))
        {
            return GetFromCacheOrFetch(key, code, ExpirationPolicy.Duration(expiration), @override);
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <param name="expiration">The timespan in which the cache item should expire when added.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public void Add(TKey key, TValue @value, bool @override = false, TimeSpan expiration = default(TimeSpan))
        {
            Add(key, value, ExpirationPolicy.Duration(expiration), @override);
        }

        /// <summary>
        /// Adds a value to the cache associated with to a key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="value">The value.</param>
        /// <param name="expirationPolicy">The expiration policy.</param>
        /// <param name="override">Indicates if the key exists the value will be overridden.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public void Add(TKey key, TValue @value, ExpirationPolicy expirationPolicy, bool @override = false)
        {
            Argument.IsNotNull("key", key);

            if (!_storeNullValues)
            {
                Argument.IsNotNull("value", value);
            }

            GetFromCacheOrFetch(key, () => @value, expirationPolicy, @override);
        }

        /// <summary>
        /// Removes an item from the cache.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="action">The action that need to be executed in synchronization with the item cache removal.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="key" /> is <c>null</c>.</exception>
        public void Remove(TKey key, Action action = null)
        {
            Argument.IsNotNull("key", key);

            _lock.EnterWriteLock();

            try
            {
                if (_dictionary.ContainsKey(key))
                {
                    if (action != null)
                    {
                        action.Invoke();
                    }

                    _dictionary.Remove(key);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Clears all the items currently in the cache.
        /// </summary>
        public void Clear()
        {
            var keysToRemove = new List<TKey>();

            _lock.EnterWriteLock();

            try
            {
                keysToRemove.AddRange(_dictionary.Keys);

                foreach (var keyToRemove in keysToRemove)
                {
                    _dictionary.Remove(keyToRemove);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes the expired items from the cache.
        /// </summary>
        [SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1409:RemoveUnnecessaryCode", Justification = "Reviewed. Suppression is OK here.")]
        private void RemoveExpiredItems()
        {
            bool containsItemsThatCanExpire = false;

            var keysToRemove = new List<TKey>();

            //_lock.EnterUpgradeableReadLock();
            _lock.EnterWriteLock();

            try
            {
                foreach (var cacheItem in _dictionary)
                {
                    var valueInfo = cacheItem.Value;
                    if (valueInfo.IsExpired)
                    {
                        keysToRemove.Add(cacheItem.Key);
                    }

                    if (!containsItemsThatCanExpire && valueInfo.CanExpire)
                    {
                        containsItemsThatCanExpire = true;
                    }
                }

                //_lock.EnterWriteLock();

                //try
                //{
                    foreach (var keyToRemove in keysToRemove)
                    {
                        _dictionary.Remove(keyToRemove);
                    }
                //}
                //finally
                //{
                //    _lock.ExitWriteLock();
                //}

                _checkForExpiredItems = containsItemsThatCanExpire;
            }
            finally
            {
                //_lock.ExitUpgradeableReadLock();
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Called when the timer to clean up the cache elapsed.
        /// </summary>
        /// <param name="state">The timer state.</param>
        private void OnTimerElapsed(object state)
        {
            if (!_checkForExpiredItems)
            {
                return;
            }

            RemoveExpiredItems();
        }
        #endregion
    }
}