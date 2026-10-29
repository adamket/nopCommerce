using Nop.Core;

namespace Apt.Nop.Plugin.Misc.AkeneoConnection.Types;

/// <summary>
/// The variant's own product was not written for a reason that is not an
/// error: an update-only profile with no product to update, or a skip rule.
/// The variant is logged as skipped and its parent's updates are kept.
/// </summary>
public sealed class AkeneoVariantChildSkippedException()
    : NopException("The variant's nopCommerce product was not written, so the variant was skipped.");
