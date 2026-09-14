using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaskFlow.Core.DTOs;
using TaskFlow.Core.Entities;
using TaskFlow.Core.Interfaces;

namespace TaskFlow.API.Controllers
{
    /// <summary>
    /// Project management endpoints.
    /// </summary>
    /// <remarks>
    /// Projects are private to the user who created them. Every route that addresses a
    /// project by id verifies ownership and answers 404 when the caller is not the
    /// owner, so that "not yours" and "does not exist" are indistinguishable. Returning
    /// 403 instead would confirm the project exists and allow a caller to enumerate
    /// other users' data by probing ids.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    [Produces("application/json")]
    public class ProjectsController : ControllerBase
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly ILogger<ProjectsController> _logger;
        private readonly TimeProvider _timeProvider;

        /// <summary>Creates the controller.</summary>
        public ProjectsController(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            ILogger<ProjectsController> logger,
            TimeProvider timeProvider)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _logger = logger;
            _timeProvider = timeProvider;
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
        /// Get every project in the system. Administrators only.
        /// </summary>
        /// <returns>All projects, across all users</returns>
        /// <remarks>
        /// Answers 403 rather than 404 for an authenticated non-administrator. Unlike
        /// an individual project, the existence of this endpoint is not confidential -
        /// it is part of the published contract - so concealing it would reveal nothing
        /// while making a permissions problem look like a routing error.
        /// </remarks>
        [HttpGet("all")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(typeof(IEnumerable<ProjectDto>), 200)]
        [ProducesResponseType(403)]
        public async Task<ActionResult<IEnumerable<ProjectDto>>> GetAllProjects()
        {
            var projects = await _unitOfWork.Projects.GetAllAsync();
            return Ok(_mapper.Map<IEnumerable<ProjectDto>>(projects));
        }

        /// <summary>
        /// Get a specific project by ID
        /// </summary>
        /// <param name="id">Project ID</param>
        /// <returns>Project details</returns>
        [HttpGet("{id:int}")]
        [ProducesResponseType(typeof(ProjectDto), 200)]
        [ProducesResponseType(typeof(ErrorResponseDto), 404)]
        public async Task<ActionResult<ProjectDto>> GetProject(int id)
        {
            var project = await _unitOfWork.Projects.GetWithTasksAsync(id);

            if (project == null || project.CreatedById != GetCurrentUserId())
            {
                return ProjectNotFound();
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
            project.CreatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _unitOfWork.Projects.AddAsync(project);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Project {ProjectId} created by user {UserId}", project.Id, userId);

            // Re-read so the owner navigation is populated. Mapping the just-inserted
            // entity directly would leave CreatedByName null here while the same
            // project returned CreatedByName from GET, giving the two routes different
            // representations of one resource.
            var created = await _unitOfWork.Projects.GetByIdAsync(project.Id);
            var projectDto = _mapper.Map<ProjectDto>(created);

            return CreatedAtAction(nameof(GetProject), new { id = project.Id }, projectDto);
        }

        /// <summary>
        /// Update an existing project
        /// </summary>
        /// <param name="id">Project ID</param>
        /// <param name="updateProjectDto">Fields to change; omitted fields are left as they are</param>
        /// <returns>The updated project</returns>
        [HttpPut("{id:int}")]
        [ProducesResponseType(typeof(ProjectDto), 200)]
        [ProducesResponseType(typeof(ErrorResponseDto), 400)]
        [ProducesResponseType(typeof(ErrorResponseDto), 404)]
        public async Task<ActionResult<ProjectDto>> UpdateProject(
            int id,
            [FromBody] UpdateProjectDto updateProjectDto)
        {
            var userId = GetCurrentUserId();
            var project = await _unitOfWork.Projects.GetByIdAsync(id);

            if (project == null || project.CreatedById != userId)
            {
                return ProjectNotFound();
            }

            // Patch semantics: the mapping applies only the members the client supplied.
            _mapper.Map(updateProjectDto, project);
            project.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _unitOfWork.Projects.UpdateAsync(project);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Project {ProjectId} updated by user {UserId}", id, userId);

            return Ok(_mapper.Map<ProjectDto>(project));
        }

        /// <summary>
        /// Delete a project and everything belonging to it
        /// </summary>
        /// <param name="id">Project ID</param>
        /// <remarks>
        /// The project's tasks are removed with it, by the cascade delete configured on
        /// the relationship.
        /// </remarks>
        [HttpDelete("{id:int}")]
        [ProducesResponseType(204)]
        [ProducesResponseType(typeof(ErrorResponseDto), 404)]
        public async Task<IActionResult> DeleteProject(int id)
        {
            var userId = GetCurrentUserId();
            var project = await _unitOfWork.Projects.GetByIdAsync(id);

            if (project == null || project.CreatedById != userId)
            {
                return ProjectNotFound();
            }

            await _unitOfWork.Projects.DeleteAsync(project);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Project {ProjectId} deleted by user {UserId}", id, userId);

            return NoContent();
        }

        /// <summary>
        /// The single response used for both "no such project" and "not your project".
        /// </summary>
        private NotFoundObjectResult ProjectNotFound() =>
            NotFound(new ErrorResponseDto { Message = "Project not found" });

        private int GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.Parse(userIdClaim ?? "0");
        }
    }
}
