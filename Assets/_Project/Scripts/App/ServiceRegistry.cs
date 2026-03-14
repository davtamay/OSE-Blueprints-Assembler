using System;
using System.Collections.Generic;

namespace OSE.App
{
    public static class ServiceRegistry
    {
        private static readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

        public static void Register<T>(T instance) where T : class
        {
            if (instance == null)
                throw new ArgumentNullException(nameof(instance));
            _services[typeof(T)] = instance;
        }

        public static T Get<T>() where T : class
        {
            if (_services.TryGetValue(typeof(T), out var service))
                return (T)service;
            throw new InvalidOperationException($"Service not registered: {typeof(T).Name}");
        }

        public static bool TryGet<T>(out T service) where T : class
        {
            if (_services.TryGetValue(typeof(T), out var obj))
            {
                service = (T)obj;
                return true;
            }
            service = null;
            return false;
        }

        public static bool IsRegistered<T>() where T : class =>
            _services.ContainsKey(typeof(T));

        public static bool Unregister<T>() where T : class =>
            _services.Remove(typeof(T));

        public static void Clear() => _services.Clear();
    }
}
