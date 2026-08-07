using System;

namespace PowerOfFire.DrawToPlay
{
    /// <summary>The executor's view of a typed service reference — what the StartTree
    /// injection pass talks to without knowing the generic argument, exactly like the
    /// registry-reference seam.</summary>
    public interface IStateTreeServiceRef
    {
        Type ServiceType { get; }

        void Bind(object service);
    }

    /// <summary>
    /// A task's typed CAPABILITY requirement (M15) — the third self-describing wire field,
    /// after entries and keys: <c>public StateTreeServiceRef&lt;IWorldQuery&gt; world;</c>
    /// declares what a task needs, and the executor injects it at StartTree from the
    /// OWNER's view of the spine — the same tree mounted under two Player hosts gets each
    /// player's own service, and the tree never knows. Nothing is serialized: which
    /// instance answers is a runtime fact of where the tree runs, never authored state.
    /// A spine that provides no such capability is one error and a null the task's own
    /// guard answers for.
    /// </summary>
    [Serializable]
    public sealed class StateTreeServiceRef<T> : IStateTreeServiceRef
        where T : class
    {
        [NonSerialized]
        private T m_Service;

        /// <summary>The live service, injected at StartTree. Null until then, or when the
        /// spine provides no <typeparamref name="T"/>.</summary>
        public T service => m_Service;

        Type IStateTreeServiceRef.ServiceType => typeof(T);

        void IStateTreeServiceRef.Bind(object service)
        {
            m_Service = service as T;
        }
    }
}
