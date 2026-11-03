using H.Sales.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace H.Sales.Permissions;

/// <summary>
/// SoftPlan catalog already registers Sales.* screen actions.
/// Keep this empty to avoid duplicate permission name collisions.
/// </summary>
public class SalesPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        // no-op — SoftPlanPermissionDefinitionProvider owns Sales.* names
    }

    private static LocalizableString L(string name) =>
        LocalizableString.Create<SalesResource>(name);
}
