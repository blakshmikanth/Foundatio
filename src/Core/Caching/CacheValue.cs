﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Foundatio.Caching {
    public class CacheValue<T> {
        public CacheValue(T value, bool hasValue) {
            Value = value;
            HasValue = hasValue;
        }

        public bool HasValue { get; private set; }

        public T Value { get; private set; }

        public static CacheValue<T> Null => new CacheValue<T>(default(T), false);
    }
}