using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TodoApi.Data;
using TodoApi.Dtos;
using TodoApi.Models;

namespace TodoApi.Tests.IntegrationTests;

public class TodosTests : IntegrationTestBase
{
    private async Task<long> CreateTestTodoList(string name = "Test List")
    {
        using var scope = CreateServiceScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();

        var todoList = new TodoList { Name = name };
        context.TodoList.Add(todoList);
        await context.SaveChangesAsync();

        return todoList.Id;
    }

    [Fact]
    public async Task CreateTodoItem_WithValidData_ReturnsOkWithCreatedItem()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createDto = new CreateTodoItem("Buy groceries", false);

        // Act
        var response = await PostAsync($"/api/todolists/{todoListId}/todos", createDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateTodoItemResult>();
        Assert.NotNull(result);
        Assert.True(result.id > 0);
        Assert.Equal("Buy groceries", result.description);
    }

    [Fact]
    public async Task CreateTodoItem_WithHtmlInDescription_SanitizesHtml()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createDto = new CreateTodoItem("<script>alert('xss')</script>Clean the house", false);

        // Act
        var response = await PostAsync($"/api/todolists/{todoListId}/todos", createDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateTodoItemResult>();
        Assert.NotNull(result);
        Assert.DoesNotContain("<script>", result.description);
    }

    [Fact]
    public async Task CreateTodoItem_WithInvalidTodoListId_ReturnsNotFound()
    {
        // Arrange
        var createDto = new CreateTodoItem("Buy groceries", false);

        // Act
        var response = await PostAsync("/api/todolists/999999/todos", createDto);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodoItem_WithNegativeTodoListId_ReturnsInternalServerError()
    {
        // Arrange
        var createDto = new CreateTodoItem("Buy groceries", false);

        // Act
        var response = await PostAsync("/api/todolists/-1/todos", createDto);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<ErrorDto>();
        Assert.NotNull(result);
        Assert.Equal(500, result.code);
    }

    [Fact]
    public async Task CreateTodoItem_WithEmptyDescription_ReturnsBadRequest()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createDto = new CreateTodoItem("", false);

        // Act
        var response = await PostAsync($"/api/todolists/{todoListId}/todos", createDto);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateTodoItem_WithCompletedTrue_CreatesCompletedItem()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createDto = new CreateTodoItem("Already done task", true);

        // Act
        var response = await PostAsync($"/api/todolists/{todoListId}/todos", createDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CreateTodoItemResult>();
        Assert.NotNull(result);
        Assert.Equal("Already done task", result.description);
    }

    [Fact]
    public async Task UpdateTodoItem_WithValidData_ReturnsOkWithUpdatedItem()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createResponse = await PostAsync($"/api/todolists/{todoListId}/todos",
            new CreateTodoItem("Original description", false));
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateTodoItemResult>();

        var updateDto = new UpdateTodoItem("Updated description", true);

        // Act
        var response = await PutAsync($"/api/todolists/{todoListId}/todos/{createResult!.id}", updateDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UpdateTodoItemResult>();
        Assert.NotNull(result);
        Assert.Equal("Updated description", result.description);
        Assert.True(result.completed);
    }

    [Fact]
    public async Task UpdateTodoItem_WithHtmlInDescription_SanitizesHtml()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createResponse = await PostAsync($"/api/todolists/{todoListId}/todos",
            new CreateTodoItem("Original description", false));
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateTodoItemResult>();

        var updateDto = new UpdateTodoItem("<img src=x onerror=alert(1)>Updated", false);

        // Act
        var response = await PutAsync($"/api/todolists/{todoListId}/todos/{createResult!.id}", updateDto);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UpdateTodoItemResult>();
        Assert.NotNull(result);
        Assert.DoesNotContain("onerror", result.description);
        Assert.DoesNotContain("alert", result.description);
    }

    [Fact]
    public async Task UpdateTodoItem_WithInvalidTodoListId_ReturnsNotFound()
    {
        // Arrange
        var updateDto = new UpdateTodoItem("Updated description", true);

        // Act
        var response = await PutAsync("/api/todolists/999999/todos/1", updateDto);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTodoItem_WithInvalidTodoItemId_ReturnsNotFound()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var updateDto = new UpdateTodoItem("Updated description", true);

        // Act
        var response = await PutAsync($"/api/todolists/{todoListId}/todos/999999", updateDto);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTodoItem_WithEmptyDescription_ReturnsBadRequest()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createResponse = await PostAsync($"/api/todolists/{todoListId}/todos",
            new CreateTodoItem("Original description", false));
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateTodoItemResult>();

        var updateDto = new UpdateTodoItem("", true);

        // Act
        var response = await PutAsync($"/api/todolists/{todoListId}/todos/{createResult!.id}", updateDto);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTodoItem_WithValidId_ReturnsNoContent()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createResponse = await PostAsync($"/api/todolists/{todoListId}/todos",
            new CreateTodoItem("To be deleted", false));
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateTodoItemResult>();

        // Act
        var response = await DeleteAsync($"/api/todolists/{todoListId}/todos/{createResult!.id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTodoItem_WithInvalidTodoListId_ReturnsNotFound()
    {
        // Act
        var response = await DeleteAsync("/api/todolists/999999/todos/1");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTodoItem_WithInvalidTodoItemId_ReturnsNotFound()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();

        // Act
        var response = await DeleteAsync($"/api/todolists/{todoListId}/todos/999999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTodoItem_VerifyItemIsDeleted()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        var createResponse = await PostAsync($"/api/todolists/{todoListId}/todos",
            new CreateTodoItem("To be deleted", false));
        var createResult = await createResponse.Content.ReadFromJsonAsync<CreateTodoItemResult>();

        // Act
        await DeleteAsync($"/api/todolists/{todoListId}/todos/{createResult!.id}");

        // Assert
        using var scope = CreateServiceScope();
        var context = scope.ServiceProvider.GetRequiredService<TodoContext>();
        var deletedItem = await context.TodoList
            .Where(tl => tl.Id == todoListId)
            .SelectMany(tl => tl.TodoItems)
            .FirstOrDefaultAsync(ti => ti.Id == createResult.id);

        Assert.Null(deletedItem);
    }

    [Fact]
    public async Task GetTodoItems_WithValidTodoListId_ReturnsAllItems()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();
        await PostAsync($"/api/todolists/{todoListId}/todos", new CreateTodoItem("Item 1", false));
        await PostAsync($"/api/todolists/{todoListId}/todos", new CreateTodoItem("Item 2", true));
        await PostAsync($"/api/todolists/{todoListId}/todos", new CreateTodoItem("Item 3", false));

        // Act
        var response = await GetAsync<List<TodoItem>>($"/api/todolists/{todoListId}/todos");

        // Assert
        Assert.NotNull(response);
        Assert.Equal(3, response.Count);
        Assert.Contains(response, item => item is { Description: "Item 1", Completed: false });
        Assert.Contains(response, item => item is { Description: "Item 2", Completed: true });
        Assert.Contains(response, item => item is { Description: "Item 3", Completed: false });
    }

    [Fact]
    public async Task GetTodoItems_WithEmptyList_ReturnsEmptyArray()
    {
        // Arrange
        var todoListId = await CreateTestTodoList();

        // Act
        var response = await GetAsync<List<TodoItem>>($"/api/todolists/{todoListId}/todos");

        // Assert
        Assert.NotNull(response);
        Assert.Empty(response);
    }

    [Fact]
    public async Task GetTodoItems_WithInvalidTodoListId_ReturnsNotFound()
    {
        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            async () => await GetAsync<List<TodoItem>>("/api/todolists/999999/todos")
        );

        Assert.Contains("404", exception.Message);
    }
    
    
}