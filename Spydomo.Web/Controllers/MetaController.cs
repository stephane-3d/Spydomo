using Microsoft.AspNetCore.Mvc;
using Spydomo.DTO;
using Spydomo.Infrastructure;


namespace Spydomo.Web.Controllers
{
    [ApiController]
    [Route("api/meta")]
    public class MetaController : ControllerBase
    {
        private readonly MetaService _meta;

        public MetaController(MetaService meta) => _meta = meta;

        [HttpGet("countries")]
        public async Task<ActionResult<List<CountryDto>>> GetCountries(CancellationToken ct)
            => Ok(await _meta.GetCountriesAsync(ct));

        [HttpGet("regions/{countryCode}")]
        public async Task<ActionResult<List<RegionDto>>> GetRegions(string countryCode, CancellationToken ct)
            => Ok(await _meta.GetRegionsAsync(countryCode, ct));
    }

}
