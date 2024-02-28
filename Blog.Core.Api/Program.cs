// 以下为asp.net 6.0的写法，如果用5.0，请看Program.five.cs文件，
// 或者参考github上的.net6.0分支相关代码

using Autofac;
using Autofac.Extensions.DependencyInjection;
using Blog.Core;
using Blog.Core.Common;
using Blog.Core.Common.Core;
using Blog.Core.Common.Helper;
using Blog.Core.Extensions;
using Blog.Core.Extensions.Apollo;
using Blog.Core.Extensions.Middlewares;
using Blog.Core.Extensions.ServiceExtensions;
using Blog.Core.Filter;
using Blog.Core.Hubs;
using Blog.Core.Serilog.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Serilog;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1、配置host与容器
builder.Host
    .UseServiceProviderFactory(new AutofacServiceProviderFactory())
    .ConfigureContainer<ContainerBuilder>(builder =>
    {
        builder.RegisterModule(new AutofacModuleRegister());

        builder.RegisterModule<AutofacPropertityModuleReg>();
    })
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        hostingContext.Configuration.ConfigureApplication();

        config.Sources.Clear();

        config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        config.AddConfigurationApollo("appsettings.apollo.json");
    });

builder.ConfigureApplication();

// 2、配置服务
builder.Services.AddSingleton(new AppSettings(builder.Configuration));

builder.Services.AddAllOptionRegister();

//解压前端UI压缩文件
builder.Services.AddUiFilesZipSetup(builder.Environment);

//是否启用IDS4权限方案 true：采用IDS4 false：采用JWT
Permissions.IsUseIds4 =
    AppSettings.app(new string[] { "Startup", "IdentityServer4", "Enabled" }).ObjToBool();

//是否启用Authing权限方案 true：采用Authing false：采用JWT
Permissions.IsUseAuthing =
    AppSettings.app(new string[] { "Startup", "Authing", "Enabled" }).ObjToBool();

RoutePrefix.Name =
    AppSettings.app(new string[] { "AppSettings", "SvcName" }).ObjToString();

JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

//缓存
builder.Services.AddCacheSetup();

//ORM SqlSugar
builder.Services.AddSqlsugarSetup();

//数据库初始化 1、种子数据 2、连接
builder.Services.AddDbSetup();

//应用服务 1、种子数据 2、QuartzJob 3、Consul 4、EventBus
builder.Services.AddInitializationHostServiceSetup();

//日志
builder.Host.AddSerilogSetup();

//AutoMapper
builder.Services.AddAutoMapperSetup();

//跨域
builder.Services.AddCorsSetup();

//性能分析
builder.Services.AddMiniProfilerSetup();

//swagger
builder.Services.AddSwaggerSetup();

//任务调度
builder.Services.AddJobSetup();

//HttpContext
builder.Services.AddHttpContextSetup();

//程序启动 重要！！！
builder.Services.AddAppTableConfigSetup(builder.Environment);

//cors
builder.Services.AddHttpPollySetup();

//nacos Dynamic Naming and Configuration Service 服务注册发现配置中心
builder.Services.AddNacosSetup(builder.Configuration);

//Redis队列
builder.Services.AddRedisInitMqSetup();

//IPLimit限流
builder.Services.AddIpPolicyRateLimitSetup(builder.Configuration);

//SignalR
builder.Services.AddSignalR().AddNewtonsoftJsonProtocol();

//授权配置
builder.Services.AddAuthorizationSetup();

if (Permissions.IsUseIds4 || Permissions.IsUseAuthing)
{
    if (Permissions.IsUseIds4)
    {
        builder.Services.AddAuthentication_Ids4Setup();
    }
    else if (Permissions.IsUseAuthing)
    {
        builder.Services.AddAuthentication_AuthingSetup();
    }
}
else
{
    builder.Services.AddAuthentication_JWTSetup();
}

builder.Services.AddScoped<UseServiceDIAttribute>();

builder.Services
    .Configure<KestrelServerOptions>(x => x.AllowSynchronousIO = true)
    .Configure<IISServerOptions>(x => x.AllowSynchronousIO = true);

builder.Services.AddSession();

builder.Services.AddControllers(o =>
    {
        o.Filters.Add(typeof(GlobalExceptionsFilter));
        //o.Conventions.Insert(0, new GlobalRouteAuthorizeConvention());
        o.Conventions.Insert(0, new GlobalRoutePrefixFilter(new RouteAttribute(RoutePrefix.Name)));
    })
    .AddNewtonsoftJson(options =>
    {
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        options.SerializerSettings.ContractResolver = new DefaultContractResolver();
        options.SerializerSettings.DateFormatString = "yyyy-MM-dd HH:mm:ss";
        //options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        options.SerializerSettings.DateTimeZoneHandling = DateTimeZoneHandling.Local;
        options.SerializerSettings.Converters.Add(new StringEnumConverter());
        //将long类型转为string
        options.SerializerSettings.Converters.Add(new NumberConverter(NumberConverterShip.Int64));
    });

//RabbitMQ
builder.Services.AddRabbitMQSetup();

//Kafka
builder.Services.AddKafkaSetup(builder.Configuration);

//EventBus事件总线
builder.Services.AddEventBusSetup();

builder.Services.AddEndpointsApiExplorer();

builder.Services.Replace(ServiceDescriptor.Transient<IControllerActivator, ServiceBasedControllerActivator>());

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// 3、配置中间件
var app = builder.Build();

IdentityModelEventSource.ShowPII = true;

app.ConfigureApplication();

app.UseApplicationSetup();

app.UseResponseBodyRead();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Error");
    //app.UseHsts();
}

//指定接口入参解密
app.UseEncryptionRequest();

//指定接口出参解密
app.UseEncryptionResponse();

//异常处理
app.UseExceptionHandlerMiddle();

//IP限流
app.UseIpLimitMiddle();

//请求、响应 日志记录
app.UseRequestResponseLogMiddle();

//用户操作记录
app.UseRecordAccessLogsMiddle();

//SignalR
app.UseSignalRSendMiddle();

//IP请求记录
app.UseIpLogMiddle();

//打印所有注入的服务
app.UseAllServicesMiddle(builder.Services);

app.UseSession();

app.UseSwaggerAuthorized();

app.UseSwaggerMiddle(() => 
    Assembly.GetExecutingAssembly().GetManifestResourceStream("Blog.Core.Api.index.html"));

app.UseCors(AppSettings.app(new string[] { "Startup", "Cors", "PolicyName" }));

DefaultFilesOptions defaultFilesOptions = new DefaultFilesOptions();

defaultFilesOptions.DefaultFileNames.Clear();

defaultFilesOptions.DefaultFileNames.Add("index.html");

app.UseDefaultFiles(defaultFilesOptions);

app.UseStaticFiles();

app.UseCookiePolicy();

app.UseStatusCodePages();

//Serilog
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = SerilogRequestUtility.HttpMessageTemplate;
    options.GetLevel = SerilogRequestUtility.GetRequestLevel;
    options.EnrichDiagnosticContext = SerilogRequestUtility.EnrichFromRequest;
});

app.UseRouting();

if (builder.Configuration.GetValue<bool>("AppSettings:UseLoadTest"))
{
    app.UseMiddleware<ByPassAuthMiddleware>();
}

app.UseAuthentication();

app.UseAuthorization();

app.UseMiniProfilerMiddleware();

app.MapControllers();

app.MapHub<ChatHub>("/api2/chatHub");

// 4、运行
app.Run();