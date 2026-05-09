using Apt.Nop.Plugin.Misc.TopicsPlus.Domain;
using Apt.Nop.Plugin.Misc.TopicsPlus.Models;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Topics;
using Nop.Services.Customers;
using Nop.Services.Helpers;
using Nop.Services.Topics;
using Nop.Web.Areas.Admin.Infrastructure.Mapper.Extensions;
using Nop.Web.Framework.Models.Extensions;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Factories;
public class TopicRevisionModelFactory : ITopicRevisionModelFactory
{
    private readonly IDateTimeHelper _dateTimeHelper;
    private readonly ITopicRevisionService _topicRevisionService;
    private readonly ICustomerService _customerService;
    private readonly ITopicService _topicService;

    public TopicRevisionModelFactory(
        IDateTimeHelper dateTimeHelper,
        ITopicRevisionService topicRevisionService, ICustomerService customerService, ITopicService topicService)
    {
        _dateTimeHelper = dateTimeHelper;
        _topicRevisionService = topicRevisionService;
        _customerService = customerService;
        _topicService = topicService;
    }

    public Task<TopicRevisionSearchModel> PrepareTopicRevisionSearchModelAsync(TopicRevisionSearchModel searchModel)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        searchModel.SetGridPageSize();

        return Task.FromResult(searchModel);
    }

    public async Task<TopicRevisionListModel> PrepareTopicRevisionListModelAsync(TopicRevisionSearchModel searchModel, Topic topic = null)
    {
        ArgumentNullException.ThrowIfNull(searchModel);

        topic ??= await _topicService.GetTopicByIdAsync(searchModel.SearchTopicId);

        var revisions = await _topicRevisionService.SearchTopicRevisionsAsync(
            topicId: searchModel.SearchTopicId,
            pageIndex: searchModel.Page - 1,
            pageSize: searchModel.PageSize);

        var customers = await _customerService.GetCustomersByIdsAsync(revisions.Select(q => q.CustomerId).ToArray());

        var model = await new TopicRevisionListModel().PrepareToGridAsync(searchModel, revisions, () =>
        {
            return revisions.SelectAwait(async revision =>
            {
                var customer = customers.FirstOrDefault(q => q.Id == revision.CustomerId);
                var rowModel = await PrepareTopicRevisionModelAsync(null, revision, topic, customer,true);
                return rowModel;
            });
        });

        return model;
    }

    public async Task<TopicRevisionModel> PrepareTopicRevisionModelAsync(
        TopicRevisionModel model,
        TopicRevision revision,
        Topic topic,
        Customer customer,
        bool excludeProperties = false)
    {
        if (revision != null)
        {
            var fullName = customer != null ? await _customerService.GetCustomerFullNameAsync(customer) : string.Empty;
            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = customer?.Email;
            }

            model ??= new TopicRevisionModel
            {
                Id = revision.Id,
                CustomerId = revision.CustomerId,
                Title = revision.Title, 
                CreatedOn = (await _dateTimeHelper.ConvertToUserTimeAsync(revision.CreatedOnUtc, DateTimeKind.Utc)).ToString("g"),
                TopicId = revision.TopicId,
                CustomerName = fullName,
                Version = revision.Version,
                HideRevertButton = revision.Body == topic.Body && revision.Title == topic.Title
                
            };
        }

        model ??= new TopicRevisionModel();

        return model;
    }
}
