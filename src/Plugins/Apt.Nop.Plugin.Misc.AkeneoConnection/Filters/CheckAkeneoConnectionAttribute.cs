using Apt.Nop.Plugin.Misc.AkeneoConnection.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Nop.Services.Logging;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Filters;

/// <summary>
/// Represents a filter attribute that checks connectivity to the configured
/// Akeneo instance.
/// </summary>
public sealed class CheckAkeneoConnectionAttribute : TypeFilterAttribute
{
    #region Ctor

    /// <summary>
    /// Creates an instance of the filter attribute.
    /// </summary>
    /// <param name="ignore">Whether to ignore execution of the filter.</param>
    public CheckAkeneoConnectionAttribute(bool ignore = false)
        : base(typeof(CheckAkeneoConnectionFilter))
    {
        IgnoreFilter = ignore;
        Arguments = [ignore];
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets a value indicating whether to ignore execution of the filter.
    /// </summary>
    public bool IgnoreFilter { get; }

    #endregion

    #region Nested filter

    /// <summary>
    /// Represents a filter that checks connectivity to the configured
    /// Akeneo instance.
    /// </summary>
    private class CheckAkeneoConnectionFilter : IAsyncActionFilter
    {
        #region Fields

        private const string ConnectionValidArgumentName = "connectionValid";

        protected readonly IAkeneoApiClient _akeneoApiClient;
        protected readonly bool _ignoreFilter;
        protected readonly ILogger _logger;

        #endregion

        #region Ctor

        public CheckAkeneoConnectionFilter(
            bool ignoreFilter,
            IAkeneoApiClient akeneoApiClient,
            ILogger logger)
        {
            _ignoreFilter = ignoreFilter;
            _akeneoApiClient = akeneoApiClient;
            _logger = logger;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Checks connectivity to Akeneo and assigns the result to the
        /// connectionValid action argument.
        /// </summary>
        /// <param name="context">A context for action filters.</param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        private async Task CheckAkeneoConnectionAsync(
            ActionExecutingContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // Check whether this filter has been overridden for the action.
            var actionFilter = context.ActionDescriptor.FilterDescriptors
                .Where(filterDescriptor =>
                    filterDescriptor.Scope == FilterScope.Action)
                .Select(filterDescriptor => filterDescriptor.Filter)
                .OfType<CheckAkeneoConnectionAttribute>()
                .FirstOrDefault();

            // Ignore the filter when explicitly disabled on the action.
            if (actionFilter?.IgnoreFilter ?? _ignoreFilter)
                return;

            // Only set the argument when the action declares a bool parameter
            // named connectionValid.
            var connectionValidParameter = context.ActionDescriptor.Parameters
                .OfType<ControllerParameterDescriptor>()
                .FirstOrDefault(parameter =>
                    parameter.ParameterInfo.ParameterType == typeof(bool) &&
                    parameter.Name.Equals(
                        ConnectionValidArgumentName,
                        StringComparison.OrdinalIgnoreCase));

            if (connectionValidParameter == null)
            {
                return;
                //I do not care about this
                //throw new InvalidOperationException(
                //    $"Actions annotated with {nameof(CheckAkeneoConnectionAttribute)} " +
                //    "must declare a bool connectionValid parameter.");
            }

            var connectionValid = false;

            try
            {
                connectionValid = await _akeneoApiClient.KnockAsync(
                    context.HttpContext.RequestAborted);
            }
            catch (OperationCanceledException)
                when (context.HttpContext.RequestAborted.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                await _logger.ErrorAsync(
                    "An unexpected error occurred while checking Akeneo connectivity.",
                    exception);
            }

            // Use the actual declared parameter name in case its casing differs.
            context.ActionArguments[connectionValidParameter.Name] =
                connectionValid;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called asynchronously before the action, after model binding is complete.
        /// </summary>
        /// <param name="context">A context for action filters.</param>
        /// <param name="next">
        /// A delegate invoked to execute the next action filter or the action itself.
        /// </param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            await CheckAkeneoConnectionAsync(context);
            await next();
        }

        #endregion
    }

    #endregion
}