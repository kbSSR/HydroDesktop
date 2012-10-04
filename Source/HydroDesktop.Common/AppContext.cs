﻿using System;
using Microsoft.Practices.Unity;

namespace HydroDesktop.Common
{
    /// <summary>
    /// Context of application
    /// </summary>
    public class AppContext
    {
        private static readonly  Lazy<AppContext> _lazy = new Lazy<AppContext>(ValueFactory, true);
        private AppContext()
        {
            
        }
        private static AppContext ValueFactory()
        {
            return new AppContext();
        }

        /// <summary>
        /// Gets the instance of context.
        /// </summary>
        public static AppContext Instance
        {
            get { return _lazy.Value; }
        }

        /// <summary>
        /// Gets registered service.
        /// </summary>
        /// <typeparam name="T">Type to resolve.</typeparam>
        /// <returns>Instance of type T.</returns>
        public T Get<T>()
        {
            return Container.Current.Resolve<T>();
        }

        /// <summary>
        /// Gets current DI container.
        /// </summary>
        public IUnityContainer UnityContainer
        {
            get { return Container.Current; }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Container.Current.Dispose();
        }
    }
}
