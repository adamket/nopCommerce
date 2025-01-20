//using Apt.Nop.Plugin.Misc.NopCms.Models;
//using Microsoft.AspNetCore.Mvc;
//using Nop.Web.Areas.Admin.Models.Topics;
//using Nop.Web.Framework.Components;
//using Nop.Web.Framework.Models;

//namespace Apt.Nop.Plugin.Misc.NopCms.Components;

///// <summary>
///// Represents view component to display field to select customer roles
///// </summary>
//public partial class NopCmsAdminTopicBlockViewComponent : NopViewComponent
//{
//    #region Methods

//    public IViewComponentResult Invoke(string widgetZone, object additionalData)
//    {

//        var topicModel = (TopicModel)additionalData;
//        var searchModel = new TopicEntrySearchModel
//        {
//            TopicId = topicModel.Id
//        };

//        return View("~/Plugins/Misc.NopCms/Views/Admin/Widget/TopicDetailsBlock.cshtml", searchModel);
//    }

//    #endregion
//}