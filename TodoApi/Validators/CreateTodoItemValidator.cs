using FluentValidation;
using TodoApi.Dtos;

namespace TodoApi.Validators;

public class CreateTodoItemValidator : AbstractValidator<CreateTodoItem>
{
    public CreateTodoItemValidator()
    {
        RuleFor(r => r.Description)
            .NotEmpty()
            .WithMessage("missing_name");
    }
}
