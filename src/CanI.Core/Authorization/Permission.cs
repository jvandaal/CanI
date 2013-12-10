﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CanI.Core.Cleaners;
using CanI.Core.Configuration;
using CanI.Core.Predicates;

namespace CanI.Core.Authorization
{
    public class Permission : IPermissionConfiguration
    {
        private string AllowedAction { get; set; }
        private string AllowedSubject { get; set; }
        private readonly ActionCleaner actionCleaner;
        private readonly SubjectCleaner subjectCleaner;
        private readonly IList<IAuthorizationPredicate> authorizationPredicates;

        public Permission(string action, string subject, ActionCleaner actionCleaner, SubjectCleaner subjectCleaner)
        {
            this.actionCleaner = actionCleaner;
            this.subjectCleaner = subjectCleaner;
            AllowedAction = actionCleaner.Clean(action);
            AllowedSubject = subjectCleaner.Clean(subject);

            authorizationPredicates = new List<IAuthorizationPredicate>();
        }

        public void If(Func<bool> predicate)
        {
            authorizationPredicates.Add(new PlainAuthorizationPredicate(predicate));
        }

        public void If<T>(Func<T, bool> predicate)
        {
            authorizationPredicates.Add(new GenericPredicate<T>(predicate));
        }

        public bool Authorizes(string requestedAction, object requestedSubject)
        {
            return Matches(requestedAction, requestedSubject)
                   && ContextAllowsAction(requestedSubject)
                   && SubjectAllowsAction(requestedAction, requestedSubject);
        }

        private bool Matches(string action, object subject)
        {
            var requestedSubject = subjectCleaner.Clean(subject);
            var requestedAction = actionCleaner.Clean(action);

            return Regex.IsMatch(requestedSubject, AllowedSubject, RegexOptions.IgnoreCase)
                && Regex.IsMatch(requestedAction, AllowedAction, RegexOptions.IgnoreCase);
        }

        private bool ContextAllowsAction(object requestedSubject)
        {
            return authorizationPredicates.All(p => p.Allows(requestedSubject));
        }

        private bool SubjectAllowsAction(string requestedAction, object requestedSubject)
        {
            const BindingFlags caseInsensitivePublicInstance = BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public;
            var property = requestedSubject.GetType().GetProperty("can" + requestedAction, caseInsensitivePublicInstance);
            if (property == null) return true;

            var propertyValue = property.GetValue(requestedSubject);
            var booleanValue = propertyValue as bool?;
            return booleanValue.GetValueOrDefault();
        }

        public bool AllowsExecutionOf(object command)
        {
            var requestedCommandName = GetRequestedCommandName(command);

            // I prefer 'foreach' instead of LINQ in this case
            foreach (var subjectAlias in subjectCleaner.AliasesFor(AllowedSubject))
            foreach (var actionAlias in actionCleaner.AliasesFor(AllowedAction))
            {
                if (Regex.IsMatch(requestedCommandName, actionAlias, RegexOptions.IgnoreCase)
                    && Regex.IsMatch(requestedCommandName, subjectAlias, RegexOptions.IgnoreCase))
                    return true;
            }

            return false;
        }

        private static string GetRequestedCommandName(object command)
        {
            if (command is string) return command.ToString();
            var commandType = command.GetType();
            var attribute = commandType.GetCustomAttribute<AuthorizeIfICanAttribute>();
            return attribute == null
                ? commandType.Name
                : attribute.Action + attribute.Subject;
        }
    }
}