using System.Text.RegularExpressions;
using Apt.Nop.Plugin.Misc.TopicsPlus.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Nop.Core;
using Nop.Core.Caching;
using Nop.Core.Domain.Blogs;
using Nop.Core.Domain.Catalog;
using Nop.Core.Domain.Localization;
using Nop.Core.Domain.News;
using Nop.Core.Domain.Topics;
using Nop.Core.Domain.Vendors;
using Nop.Services.Blogs;
using Nop.Services.Catalog;
using Nop.Services.Localization;
using Nop.Services.Messages;
using Nop.Services.News;
using Nop.Services.Seo;
using Nop.Services.Topics;
using Nop.Services.Vendors;
using Nop.Web.Factories;
using Nop.Web.Models.Topics;

namespace Apt.Nop.Plugin.Misc.TopicsPlus.Factories;

public class TopicsPlusTopicModelFactory(
    ILocalizationService localizationService,
    IStoreContext storeContext,
    ICustomTopicService topicService,
    ITopicTemplateService topicTemplateService,
    IHttpContextAccessor httpContextAccessor,
     IWorkContext workContext,
     IProductService productService,
     ICategoryService categoryService,
     IManufacturerService manufacturerService,
     IVendorService vendorService,
     INewsService newsService,
     IBlogService blogService,
     IMessageTokenProvider messageTokenProvider,
    IUrlRecordService urlRecordService,
    ITokenizer tokenizer,
    IStaticCacheManager staticCacheManager)
    : TopicModelFactory(localizationService, storeContext, topicService, topicTemplateService, urlRecordService)
{
  
    public override async Task<TopicModel> PrepareTopicModelAsync(Topic topic)
    {
        var model = await base.PrepareTopicModelAsync(topic);
        if (model == null)
        {
            return null;
        }

        var baseEntity = await GetBaseEntityAsync();
        var language = await workContext.GetWorkingLanguageAsync();

        if (baseEntity == null || !ContainsTokens(model.Title, model.Body))
        {
            return model;
        }

        var ck = staticCacheManager.PrepareKeyForDefaultCache(TopicsPlusConstants.CacheKeys.PreparedContentCacheKey, topic.Id, baseEntity.Id, baseEntity.GetType().Name, language.Id);


        var (replacedTitle, replacedBody, tokens) = await staticCacheManager.GetAsync(ck, async () =>
        {
            var tokens = await GetTokensAsync(baseEntity, language);

            string title = null;
            string body = null;

            if (tokens.Any())
            {
                title = tokenizer.Replace(model.Title, tokens, false);
                body = tokenizer.Replace(model.Body, tokens, true);
            }

            return (title, body, tokens);
        });

        //model.CustomProperties["tplus-former-body"] = model.Body;
        //model.CustomProperties["tplus-former-title"] = model.Title;
        model.CustomProperties[TopicsPlusConstants.HasTokensPropertyName] = "true";

        model.Body = replacedBody;
        model.Title = replacedTitle;

        return model;
    }

    private static bool ContainsTokens(string title, string body)
    {
        return ContainsTokens(title) || ContainsTokens(body);
    }

    private static bool ContainsTokens(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(value, @"%[A-Za-z0-9_.]+%");
    }


    private async Task<BaseEntity> GetBaseEntityAsync()
    {
        var routeValues = httpContextAccessor?.HttpContext?.Request.RouteValues;
        if (routeValues == null)
        {
            return null;
        }

        var slug = routeValues["SeName"]?.ToString();

        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var urlRecord = await _urlRecordService.GetBySlugAsync(slug);
        if (urlRecord == null || !urlRecord.IsActive)
        {
            return null;
        }

        var entityId = urlRecord.EntityId;
        var entityType = urlRecord.EntityName;

        switch (entityType)
        {
            case nameof(Product):
                return await productService.GetProductByIdAsync(entityId);
               
            case nameof(Category):
                return await categoryService.GetCategoryByIdAsync(entityId);
               
            case nameof(Manufacturer):
                return await manufacturerService.GetManufacturerByIdAsync(entityId);
              
            case nameof(Vendor):
                return await vendorService.GetVendorByIdAsync(entityId);
               
            case nameof(NewsItem):
                return await newsService.GetNewsByIdAsync(entityId);
               
            case nameof(BlogPost):
                return await blogService.GetBlogPostByIdAsync(entityId);
              
            default:
                return null;
        }
    }

    private async Task<IList<Token>> GetTokensAsync<T>(T baseEntity, Language language) where T : BaseEntity
    {
        var tokens = new List<Token>();
        switch (baseEntity)
        {
            case Product product:
                await messageTokenProvider.AddProductTokensAsync(tokens, product, language.Id);
                break;
            case Category category:
                // await messageTokenProvider.AddCategorfy(tokens, product, language.Id);
                break;
            case Manufacturer manufacturer:
                //  messageTokenProvider.AddMa
                break;
            case Vendor vendor:
                break;
            case NewsItem newsItem:
                break;
            case BlogPost blogPost:
                break;
            default:
                break; 
        }
        return tokens;
    }
}
