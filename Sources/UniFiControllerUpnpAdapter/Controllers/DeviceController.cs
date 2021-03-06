﻿using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Framework.Extensions;
using UniFiControllerUpnpAdapter.Business;

namespace UniFiControllerUpnpAdapter.Controllers
{
	[Route("api/[controller]")]
	public class DeviceController : Controller
	{
		private readonly IDeviceService _devices;
		private readonly ISsdpPublishingService _ssdp;

		public DeviceController(IDeviceService devices, ISsdpPublishingService ssdp)
		{
			_devices = devices;
			_ssdp = ssdp;
		}

		[HttpGet("{id}")]
		public async Task<ActionResult> Get(string id)
		{
			var (hasDocument, document) = await _ssdp.GetDescriptionDocument(id);
			if (hasDocument)
			{
				return Content(document, "application/xml", Encoding.UTF8);
			}
			else
			{
				return NotFound();
			}
		}

		[Route("{deviceId}", Order = 2)]
		public async Task<IActionResult> Subscribe(
			string deviceId,
			[FromHeader(Name = "CALLBACK")] string callbackHeader,
			[FromHeader(Name = "TIMEOUT")] string timeoutHeader)
		{
			if (!HttpContext.Request.Method.Equals("SUBSCRIBE", StringComparison.OrdinalIgnoreCase))
			{
				return NotFound();
			}

			if (string.IsNullOrWhiteSpace(deviceId)
			    || string.IsNullOrWhiteSpace(callbackHeader)
			    || string.IsNullOrWhiteSpace(timeoutHeader))
			{
				return BadRequest();
			}

			var callbackUri = new Uri(callbackHeader.TrimStart('<').TrimEnd('>'), UriKind.Absolute);
			var callbackDuration = TimeSpan.FromSeconds(int.Parse(timeoutHeader.TrimStart("Second-", StringComparison.OrdinalIgnoreCase)));
			var callbackExpiration = DateTimeOffset.Now + callbackDuration;

			var callback = new Callback
			{
				Id = Guid.NewGuid().ToString("N"),
				Uri = callbackUri,
				Duration = callbackDuration,
				Expiration = callbackExpiration
			};

			await _devices.AddCallback(ControllerContext.HttpContext.RequestAborted, deviceId, callback);

			Response.Headers["SID"] = $"uuid:{callback.Id}";
			Response.Headers["SERVER"] = $"Windows/10.1706 UPnP/1.1 UniFiClientManagerServer/1.0";
			Response.Headers["TIMEOUT"] = $"Second-{callback.Duration.TotalSeconds}";
			Response.Headers["Content-Length"] = "0";
			Response.Headers["Smartthings-Device"] = deviceId;

			return Ok();
		}

		[HttpPut("{deviceId}")]
		public async Task<ActionResult> Override(string deviceId, [FromForm] PresenceState state)
		{
			await _devices.Set(CancellationToken.None, deviceId, state == PresenceState.Present);

			return Ok();
		}
	}
}
