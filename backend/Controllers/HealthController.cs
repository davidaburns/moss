namespace Moss.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Moss.Models;

[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/health")]
public class HealthController : ControllerBase {
    public ActionResult GetHealth() {
        return Ok(new HealthStatusDto("Ok"));
    }
}
