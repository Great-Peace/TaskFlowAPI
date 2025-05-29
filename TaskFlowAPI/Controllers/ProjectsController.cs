using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;
using TaskFlow.Core.Interfaces;

namespace TaskFlow.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [Produces("application/json")]
    public class ProjectsController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<ProjectsController> _logger;

        public ProjectsController(IUnitOfWork unitOfWork, IMapper mapper, ILogger<ProjectsController> logger)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
        }

        /// <summary>
        /// Get all projects for the authenticated user
        /// </summary>
        /// <returns>List of user's projects</returns>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<ProjectDto>), 200)]
        public async Task<ActionResult<IEnumerable<ProjectDto>>> GetProjects()
        {
            var userId = GetCurrentUserId();
            var projects = await _unitOfWork.Projects.GetUserProjectsAsync(userId);
            var projectDtos = _mapper.Map<IEnumerable<ProjectDto>>(projects);

            return Ok(projectDtos);
        }

        /// <summary>
        /// Get a specific project by ID
        /// </summary>
        /// <param name="id">Project ID</param>
        /// <returns>Project details</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ProjectDto), 200)]
        [ProducesResponseType(typeof(ErrorResponseDto), 404)]
        public async Task<ActionResult<ProjectDto>> GetProject(int id)
        {
            var project = await _unitOfWork.Projects.GetWithTasksAsync(id);

            if (project == null)
            {
                return NotFound(new ErrorResponseDto { Message = "Project not found" });
            }

            var projectDto = _mapper.Map<ProjectDto>(project);
            return Ok(projectDto);
        }

        /// <summary>
        /// Create a new project
        /// </summary>
        /// <param name="createProjectDto">Project creation details</param>
        /// <returns>Created project</returns>
        [HttpPost]
        [ProducesResponseType(typeof(ProjectDto), 201)]
        [ProducesResponseType(typeof(ErrorResponseDto), 400)]
        public async Task<ActionResult<ProjectDto>> CreateProject([FromBody] CreateProjectDto createProjectDto)
        {
            var userId = GetCurrentUserId();

            var project = _mapper.Map<Project>(createProjectDto);
            project.CreatedById = userId;

            await _unitOfWork.Projects.AddAsync(project);
            await _unitOfWork.SaveChangesAsync();

            var projectDto = _mapper.Map<ProjectDto>(project);

            _logger.LogInformation("Project {ProjectId} created by user {UserId}", project.Id, userId);

            return CreatedAtAction(nameof(GetProject), new { id = project.Id }, projectDto);
        }

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.Parse(userIdClaim ?? "0");
        }
    }
}