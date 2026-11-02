using System.Text.Json;
using DungAT.WorkflowEngine.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace DungAT.WorkflowEngine;

internal sealed record CodeDefinition(WorkflowDefinition Definition);
