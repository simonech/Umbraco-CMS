﻿using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Hosting;
using Umbraco.Core.Services;
using Umbraco.Core.Sync;
using Umbraco.Web.Routing;

namespace Umbraco.Web.Scheduling
{
    public sealed class SchedulerComponent : IComponent
    {
        private const int DefaultDelayMilliseconds = 180000; // 3 mins
        private const int OneMinuteMilliseconds = 60000;

        private readonly IRuntimeState _runtime;
        private readonly IMainDom _mainDom;
        private readonly IServerRegistrar _serverRegistrar;
        private readonly IContentService _contentService;
        private readonly ILogger<SchedulerComponent> _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IApplicationShutdownRegistry _applicationShutdownRegistry;
        private readonly IUmbracoContextFactory _umbracoContextFactory;
        private readonly IServerMessenger _serverMessenger;
        private readonly IRequestAccessor _requestAccessor;
        private readonly IBackofficeSecurityFactory _backofficeSecurityFactory;

        private BackgroundTaskRunner<IBackgroundTask> _publishingRunner;

        private bool _started;
        private object _locker = new object();
        private IBackgroundTask[] _tasks;

        public SchedulerComponent(IRuntimeState runtime, IMainDom mainDom, IServerRegistrar serverRegistrar,
            IContentService contentService, IUmbracoContextFactory umbracoContextFactory, ILoggerFactory loggerFactory,
            IApplicationShutdownRegistry applicationShutdownRegistry,
            IServerMessenger serverMessenger, IRequestAccessor requestAccessor,
            IBackofficeSecurityFactory backofficeSecurityFactory)
        {
            _runtime = runtime;
            _mainDom = mainDom;
            _serverRegistrar = serverRegistrar;
            _contentService = contentService;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<SchedulerComponent>();
            _applicationShutdownRegistry = applicationShutdownRegistry;
            _umbracoContextFactory = umbracoContextFactory;
            _serverMessenger = serverMessenger;
            _requestAccessor = requestAccessor;
            _backofficeSecurityFactory = backofficeSecurityFactory;
        }

        public void Initialize()
        {
            var logger = _loggerFactory.CreateLogger<BackgroundTaskRunner<IBackgroundTask>>();
            // backgrounds runners are web aware, if the app domain dies, these tasks will wind down correctly
            _publishingRunner = new BackgroundTaskRunner<IBackgroundTask>("ScheduledPublishing", logger, _applicationShutdownRegistry);

            // we will start the whole process when a successful request is made
            _requestAccessor.RouteAttempt += RegisterBackgroundTasksOnce;
        }

        public void Terminate()
        {
            // the AppDomain / maindom / whatever takes care of stopping background task runners
        }

        private void RegisterBackgroundTasksOnce(object sender, RoutableAttemptEventArgs e)
        {
            switch (e.Outcome)
            {
                case EnsureRoutableOutcome.IsRoutable:
                case EnsureRoutableOutcome.NotDocumentRequest:
                    _requestAccessor.RouteAttempt -= RegisterBackgroundTasksOnce;
                    RegisterBackgroundTasks();
                    break;
            }
        }

        private void RegisterBackgroundTasks()
        {
            LazyInitializer.EnsureInitialized(ref _tasks, ref _started, ref _locker, () =>
            {
                _logger.LogDebug("Initializing the scheduler");

                var tasks = new List<IBackgroundTask>();

                tasks.Add(RegisterScheduledPublishing());

                return tasks.ToArray();
            });
        }

        private IBackgroundTask RegisterScheduledPublishing()
        {
            // scheduled publishing/unpublishing
            // install on all, will only run on non-replica servers
            var task = new ScheduledPublishing(_publishingRunner, DefaultDelayMilliseconds, OneMinuteMilliseconds, _runtime, _mainDom, _serverRegistrar, _contentService, _umbracoContextFactory, _loggerFactory.CreateLogger<ScheduledPublishing>(), _serverMessenger, _backofficeSecurityFactory);
            _publishingRunner.TryAdd(task);
            return task;
        }
    }
}