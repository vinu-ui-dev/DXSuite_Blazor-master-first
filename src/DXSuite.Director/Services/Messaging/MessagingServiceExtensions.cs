using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DXSuite.Director.Services.Messaging;

public static class MessagingServiceExtensions
{
    public static IServiceCollection AddMessaging(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<MessagingOptions>(config.GetSection(MessagingOptions.SectionName));
        // Singleton: arbitration holds no state and reads only per-instance settings.
        services.AddSingleton<MessageArbitration>();
        // Configuration > Message Types, and the per-type languages the Messages pages show (MSG-03).
        services.AddScoped<IMessageLanguageCatalogue, MessageLanguageCatalogue>();
        services.AddScoped<MessageTypeConfiguration>();
        // Writing canned Messages; scoped because it acts as the signed-in author.
        services.AddScoped<MessageAuthoring>();
        // A Message Type's Access Rules (ZONE-07) and the rule check every send dialog uses (ZONE-08).
        services.AddScoped<MessageAccessRules>();
        services.AddScoped<IMessageSendAccess, MessageSendAccess>();
        // The send dialog's checks and the one-transaction Activation write; acts as the signed-in sender.
        services.AddScoped<MessageSending>();
        // What each Activation put on screens (AUD-06), written by sends and approvals; scoped for the tenant's media root.
        services.AddScoped<ShownVersions>();
        // Which clock each display reads scheduled sends against (MSG-16); singleton because it holds only settings.
        services.AddSingleton<DisplayTimeZones>();
        // The delivery document the Moxie script pulls (INK-03 to INK-08); scoped for the tenant's database.
        services.AddScoped<MessageDelivery>();
        // Every Director save that adds or deletes a display moves the messaging delivery version (INK-09).
        services.AddSingleton<IDirectorSaveChangesInterceptor, DeliveryVersionInterceptor>();
        // The Active Messages tab and the one-transaction cancel (MSG-18); acts as the signed-in user.
        services.AddScoped<ActiveMessages>();
        // A Message Type's approval chain (ZONE-09), the Access dialog's Approval Workflows section.
        services.AddScoped<MessageApprovalChain>();
        // Pending revisions and the Messages section of My Queue (MSG-11); acts as the signed-in approver.
        services.AddScoped<MessageApprovals>();
        // activation.ended, logged once per Activation by one instance at a time under a lease (AUD-08, HOST-04).
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<ActivationEndedLog>();
        services.AddHostedService<ActivationEndedService>();
        // The doorbell that nudges the Moxie script on a delivery change (INK-09): one leased job per tenant, and the
        // scoped check it runs in each tenant's scope.
        services.AddHttpClient(MessagingDoorbell.HttpClientName);
        services.AddScoped<MessagingDoorbell>();
        services.AddHostedService<MessagingDoorbellService>();
        return services;
    }
}
