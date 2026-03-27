using System;
using System.Collections.Generic;
using System.Text;

namespace OSE.App
{
    /// <summary>
    /// Lightweight service locator. MonoBehaviours self-register in
    /// <c>Awake()</c> and unregister in <c>OnDestroy()</c>.
    ///
    /// <b>Bootstrap convention:</b>
    /// <list type="bullet">
    ///   <item><c>Awake()</c> — self-register + local init only (GetComponent, field defaults).</item>
    ///   <item><c>OnEnable()</c> — resolve cross-service dependencies via <see cref="TryGet{T}"/>
    ///         and subscribe to events. OnEnable runs after all Awake calls in the same frame,
    ///         so every service is guaranteed to be registered.</item>
    ///   <item><c>Start()</c> — multi-service orchestration (e.g. Bootstrap calls that wire
    ///         several services together).</item>
    /// </list>
    /// Never call <see cref="TryGet{T}"/> for another service inside <c>Awake()</c> —
    /// Unity does not guarantee Awake ordering across GameObjects.
    /// </summary>
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

        /// <summary>Returns the number of currently registered services.</summary>
        public static int Count => _services.Count;

        /// <summary>
        /// Returns a diagnostic summary of all registered services.
        /// Intended for editor/debug use only.
        /// </summary>
        public static string GetDiagnosticSummary()
        {
            if (_services.Count == 0)
                return "[ServiceRegistry] No services registered.";

            var sb = new StringBuilder();
            sb.AppendLine($"[ServiceRegistry] {_services.Count} service(s) registered:");
            foreach (var kvp in _services)
            {
                string status = kvp.Value != null ? "OK" : "NULL";
                sb.AppendLine($"  {kvp.Key.Name} → {status}");
            }
            return sb.ToString();
        }
    }
}
