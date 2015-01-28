using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Core.Events;
using Umbraco.Core.Logging;
using Umbraco.Core.Models;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Persistence.UnitOfWork;

namespace Umbraco.Core.Services
{
    public class PublicAccessService : RepositoryService, IPublicAccessService
    {
        public PublicAccessService(IDatabaseUnitOfWorkProvider provider, RepositoryFactory repositoryFactory, ILogger logger)
            : base(provider, repositoryFactory, logger)
        {
        }

        /// <summary>
        /// Gets all defined entries and associated rules
        /// </summary>
        /// <returns></returns>
        public IEnumerable<PublicAccessEntry> GetAll()
        {
            using (var repo = RepositoryFactory.CreatePublicAccessRepository(UowProvider.GetUnitOfWork()))
            {
                return repo.GetAll();
            }
        }

        /// <summary>
        /// Gets the entry defined for the content item's path
        /// </summary>
        /// <param name="content"></param>
        /// <returns>Returns null if no entry is found</returns>
        public PublicAccessEntry GetEntryForContent(IContent content)
        {
            return GetEntryForContent(content.Path.EnsureEndsWith("," + content.Id));
        }

        /// <summary>
        /// Gets the entry defined for the content item based on a content path
        /// </summary>
        /// <param name="contentPath"></param>
        /// <returns>Returns null if no entry is found</returns>
        public PublicAccessEntry GetEntryForContent(string contentPath)
        {
            //Get all ids in the path for the content item and ensure they all
            // parse to ints that are not -1.
            var ids = contentPath.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x =>
                {
                    int val;
                    if (int.TryParse(x, out val))
                    {
                        return val;
                    }
                    return -1;
                })
                .Where(x => x != -1)
                .ToList();

            //start with the deepest id
            ids.Reverse();

            using (var repo = RepositoryFactory.CreatePublicAccessRepository(UowProvider.GetUnitOfWork()))
            {
                var entries = repo.GetEntriesForProtectedContent(ids.ToArray()).ToArray();
                //return the entry with the deepest id in the path
                foreach (var id in ids)
                {
                    var found = entries.FirstOrDefault(x => x.ProtectedNodeId == id);
                    if (found != null) return found;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if the content has an entry for it's path
        /// </summary>
        /// <param name="content"></param>
        /// <returns></returns>
        public bool IsProtected(IContent content)
        {
            return GetEntryForContent(content) != null;
        }

        /// <summary>
        /// Returns true if the content has an entry based on a content path
        /// </summary>
        /// <param name="contentPath"></param>
        /// <returns></returns>
        public bool IsProtected(string contentPath)
        {
            return GetEntryForContent(contentPath) != null;
        }

        /// <summary>
        /// Adds/updates a rule
        /// </summary>
        /// <param name="content"></param>
        /// <param name="ruleType"></param>
        /// <param name="ruleValue"></param>
        /// <returns></returns>
        public PublicAccessEntry AddOrUpdateRule(IContent content, string ruleType, string ruleValue)
        {
            var uow = UowProvider.GetUnitOfWork();
            using (var repo = RepositoryFactory.CreatePublicAccessRepository(uow))
            {
                var entry = repo.GetEntriesForProtectedContent(content.Id).FirstOrDefault();
                if (entry == null) return null;

                var existingRule = entry.Rules.FirstOrDefault(x => x.RuleType == ruleType);
                if (existingRule == null)
                {
                    entry.AddRule(ruleValue, ruleType);
                }
                else
                {
                    existingRule.RuleType = ruleType;
                    existingRule.RuleValue = ruleValue;
                }

                repo.AddOrUpdate(entry);

                uow.Commit();

                return entry;
            }
        }

        /// <summary>
        /// Removes a rule
        /// </summary>
        /// <param name="content"></param>
        /// <param name="ruleType"></param>
        /// <param name="ruleValue"></param>
        public void RemoveRule(IContent content, string ruleType, string ruleValue)
        {
            var uow = UowProvider.GetUnitOfWork();
            using (var repo = RepositoryFactory.CreatePublicAccessRepository(uow))
            {
                var entry = repo.GetEntriesForProtectedContent(content.Id).FirstOrDefault();
                if (entry == null) return;

                var existingRule = entry.Rules.FirstOrDefault(x => x.RuleType == ruleType && x.RuleValue == ruleValue);
                if (existingRule == null) return;

                entry.RemoveRule(existingRule);

                repo.AddOrUpdate(entry);

                uow.Commit();
            }
        }

        /// <summary>
        /// Saves the entry
        /// </summary>
        /// <param name="entry"></param>
        public void Save(PublicAccessEntry entry)
        {
            var uow = UowProvider.GetUnitOfWork();
            using (var repo = RepositoryFactory.CreatePublicAccessRepository(uow))
            {
                repo.AddOrUpdate(entry);
                uow.Commit();
            }
        }

        /// <summary>
        /// Deletes the entry and all associated rules
        /// </summary>
        /// <param name="entry"></param>
        public void Delete(PublicAccessEntry entry)
        {
            var uow = UowProvider.GetUnitOfWork();
            using (var repo = RepositoryFactory.CreatePublicAccessRepository(uow))
            {
                repo.Delete(entry);
                uow.Commit();
            }
        }

        /// <summary>
        /// Occurs before Save
        /// </summary>
        public static event TypedEventHandler<IPublicAccessService, SaveEventArgs<PublicAccessEntry>> Saving;

        /// <summary>
        /// Occurs after Save
        /// </summary>
        public static event TypedEventHandler<IPublicAccessService, SaveEventArgs<PublicAccessEntry>> Saved;

        /// <summary>
        /// Occurs before Delete
        /// </summary>		
        public static event TypedEventHandler<IPublicAccessService, DeleteEventArgs<PublicAccessEntry>> Deleting;

        /// <summary>
        /// Occurs after Delete
        /// </summary>
        public static event TypedEventHandler<IPublicAccessService, DeleteEventArgs<PublicAccessEntry>> Deleted;


    }
}