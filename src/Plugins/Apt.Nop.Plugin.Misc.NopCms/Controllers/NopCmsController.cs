using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Apt.Nop.Plugin.Misc.NopCms.Models;
using Apt.Nop.Plugin.Misc.NopCms.Services;
using Microsoft.AspNetCore.Mvc;
using Nop.Core.Domain.Catalog;
using Nop.Services.Helpers;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Areas.Admin.Models.Catalog;
using Nop.Web.Framework.Controllers;
using Nop.Web.Framework.Models.Extensions;

namespace Apt.Nop.Plugin.Misc.NopCms.Controllers;
public class NopCmsController(TopicEntryService _topicEntryService, IDateTimeHelper _dateTimeHelper) : BasePluginController
{
    [HttpGet("admin/nopcms/configure")]
    public async Task<IActionResult> Configure()
    {
        return View("~/plugins/Misc.NopCms/Views/Admin/Configure.chtml");
    }



    [HttpPost("admin/nopcms/topic-versions")]
    public async Task<IActionResult> TopicVersionList(TopicEntrySearchModel searchModel)
    {
        var topicEntries = await _topicEntryService.GetTopicEntriesByTopicIdAsync(searchModel.TopicId);
       

        //prepare grid model
        var model = await new TopicEntryListModel().PrepareToGridAsync(searchModel, topicEntries, () =>
        {
            return topicEntries.SelectAwait(async te =>
            {
                //fill in model values from the entity
                var topicEntryModel = new TopicEntryModel
                {
                    Id = te.Id,
                    Title = te.Title,
                    CreatedOn = (await _dateTimeHelper.ConvertToUserTimeAsync(te.CreatedOnUtc, DateTimeKind.Utc)).ToShortDateString()
                };

                return topicEntryModel;
            });
        });



        return Json(model);
    }

}
