using System;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using JsonApiDotNetCore.Serialization.Objects;
using JsonApiDotNetCoreExample;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JsonApiDotNetCoreExampleTests.IntegrationTests.AtomicOperations.Creating
{
    public sealed class AtomicCreateResourceWithToManyRelationshipTests
        : IClassFixture<IntegrationTestContext<TestableStartup<OperationsDbContext>, OperationsDbContext>>
    {
        private readonly IntegrationTestContext<TestableStartup<OperationsDbContext>, OperationsDbContext> _testContext;
        private readonly OperationsFakers _fakers = new OperationsFakers();

        public AtomicCreateResourceWithToManyRelationshipTests(IntegrationTestContext<TestableStartup<OperationsDbContext>, OperationsDbContext> testContext)
        {
            _testContext = testContext;

            testContext.ConfigureServicesAfterStartup(services =>
            {
                var part = new AssemblyPart(typeof(EmptyStartup).Assembly);
                services.AddMvcCore().ConfigureApplicationPartManager(apm => apm.ApplicationParts.Add(part));
            });
        }

        [Fact]
        public async Task Can_create_HasMany_relationship()
        {
            // Arrange
            var existingPerformers = _fakers.Performer.Generate(2);

            var newTitle = _fakers.MusicTrack.Generate().Title;

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.Performers.AddRange(existingPerformers);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            attributes = new
                            {
                                title = newTitle
                            },
                            relationships = new
                            {
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "performers",
                                            id = existingPerformers[0].StringId
                                        },
                                        new
                                        {
                                            type = "performers",
                                            id = existingPerformers[1].StringId
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("musicTracks");
            responseDocument.Results[0].SingleData.Attributes.Should().NotBeEmpty();
            responseDocument.Results[0].SingleData.Relationships.Should().NotBeEmpty();

            var newTrackId = Guid.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var trackInDatabase = await dbContext.MusicTracks
                    .Include(musicTrack => musicTrack.Performers)
                    .FirstAsync(musicTrack => musicTrack.Id == newTrackId);

                trackInDatabase.Performers.Should().HaveCount(2);
                trackInDatabase.Performers.Should().ContainSingle(performer => performer.Id == existingPerformers[0].Id);
                trackInDatabase.Performers.Should().ContainSingle(performer => performer.Id == existingPerformers[1].Id);
            });
        }

        [Fact]
        public async Task Can_create_HasManyThrough_relationship()
        {
            // Arrange
            var existingTracks = _fakers.MusicTrack.Generate(3);

            var newName = _fakers.Playlist.Generate().Name;

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.MusicTracks.AddRange(existingTracks);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "playlists",
                            attributes = new
                            {
                                name = newName
                            },
                            relationships = new
                            {
                                tracks = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "musicTracks",
                                            id = existingTracks[0].StringId
                                        },
                                        new
                                        {
                                            type = "musicTracks",
                                            id = existingTracks[1].StringId
                                        },
                                        new
                                        {
                                            type = "musicTracks",
                                            id = existingTracks[2].StringId
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("playlists");
            responseDocument.Results[0].SingleData.Attributes.Should().NotBeEmpty();
            responseDocument.Results[0].SingleData.Relationships.Should().NotBeEmpty();

            var newPlaylistId = long.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var playlistInDatabase = await dbContext.Playlists
                    .Include(playlist => playlist.PlaylistMusicTracks)
                    .ThenInclude(playlistMusicTrack => playlistMusicTrack.MusicTrack)
                    .FirstAsync(playlist => playlist.Id == newPlaylistId);

                playlistInDatabase.PlaylistMusicTracks.Should().HaveCount(3);
                playlistInDatabase.PlaylistMusicTracks.Should().ContainSingle(playlistMusicTrack => playlistMusicTrack.MusicTrack.Id == existingTracks[0].Id);
                playlistInDatabase.PlaylistMusicTracks.Should().ContainSingle(playlistMusicTrack => playlistMusicTrack.MusicTrack.Id == existingTracks[1].Id);
                playlistInDatabase.PlaylistMusicTracks.Should().ContainSingle(playlistMusicTrack => playlistMusicTrack.MusicTrack.Id == existingTracks[2].Id);
            });
        }

        [Fact]
        public async Task Cannot_create_for_missing_relationship_type()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            relationships = new
                            {
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            id = 12345678
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Request body must include 'type' element.");
            responseDocument.Errors[0].Detail.Should().Be("Expected 'type' element in 'performers' relationship.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Cannot_create_for_unknown_relationship_type()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            relationships = new
                            {
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "doesNotExist",
                                            id = 12345678
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Request body includes unknown resource type.");
            responseDocument.Errors[0].Detail.Should().Be("Resource type 'doesNotExist' does not exist.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Cannot_create_for_missing_relationship_ID()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            relationships = new
                            {
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "performers"
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Request body must include 'id' or 'lid' element.");
            responseDocument.Errors[0].Detail.Should().Be("Expected 'id' or 'lid' element in 'performers' relationship.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Cannot_create_for_unknown_relationship_IDs()
        {
            // Arrange
            var newTitle = _fakers.MusicTrack.Generate().Title;

            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            attributes = new
                            {
                                title = newTitle
                            },
                            relationships = new
                            {
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "performers",
                                            id = 12345678
                                        },
                                        new
                                        {
                                            type = "performers",
                                            id = 87654321
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.NotFound);

            responseDocument.Errors.Should().HaveCount(2);

            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[0].Title.Should().Be("A related resource does not exist.");
            responseDocument.Errors[0].Detail.Should().Be("Related resource of type 'performers' with ID '12345678' in relationship 'performers' does not exist.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");

            responseDocument.Errors[1].StatusCode.Should().Be(HttpStatusCode.NotFound);
            responseDocument.Errors[1].Title.Should().Be("A related resource does not exist.");
            responseDocument.Errors[1].Detail.Should().Be("Related resource of type 'performers' with ID '87654321' in relationship 'performers' does not exist.");
            responseDocument.Errors[1].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Cannot_create_on_relationship_type_mismatch()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            relationships = new
                            {
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "playlists",
                                            id = 12345678
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Relationship contains incompatible resource type.");
            responseDocument.Errors[0].Detail.Should().Be("Relationship 'performers' contains incompatible resource type 'playlists'.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Can_create_with_duplicates()
        {
            // Arrange
            var existingPerformer = _fakers.Performer.Generate();

            var newTitle = _fakers.MusicTrack.Generate().Title;

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                dbContext.Performers.Add(existingPerformer);
                await dbContext.SaveChangesAsync();
            });

            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            attributes = new
                            {
                                title = newTitle
                            },
                            relationships = new
                            {
                                performers = new
                                {
                                    data = new[]
                                    {
                                        new
                                        {
                                            type = "performers",
                                            id = existingPerformer.StringId
                                        },
                                        new
                                        {
                                            type = "performers",
                                            id = existingPerformer.StringId
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<AtomicOperationsDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.OK);

            responseDocument.Results.Should().HaveCount(1);
            responseDocument.Results[0].SingleData.Should().NotBeNull();
            responseDocument.Results[0].SingleData.Type.Should().Be("musicTracks");
            responseDocument.Results[0].SingleData.Attributes.Should().NotBeEmpty();
            responseDocument.Results[0].SingleData.Relationships.Should().NotBeEmpty();

            var newTrackId = Guid.Parse(responseDocument.Results[0].SingleData.Id);

            await _testContext.RunOnDatabaseAsync(async dbContext =>
            {
                var trackInDatabase = await dbContext.MusicTracks
                    .Include(musicTrack => musicTrack.Performers)
                    .FirstAsync(musicTrack => musicTrack.Id == newTrackId);

                trackInDatabase.Performers.Should().HaveCount(1);
                trackInDatabase.Performers[0].Id.Should().Be(existingPerformer.Id);
            });
        }

        [Fact]
        public async Task Cannot_create_with_null_data_in_HasMany_relationship()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "musicTracks",
                            relationships = new
                            {
                                performers = new
                                {
                                    data = (object)null
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Expected data[] element for to-many relationship.");
            responseDocument.Errors[0].Detail.Should().Be("Expected data[] element for 'performers' relationship.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }

        [Fact]
        public async Task Cannot_create_with_null_data_in_HasManyThrough_relationship()
        {
            // Arrange
            var requestBody = new
            {
                atomic__operations = new[]
                {
                    new
                    {
                        op = "add",
                        data = new
                        {
                            type = "playlists",
                            relationships = new
                            {
                                tracks = new
                                {
                                    data = (object)null
                                }
                            }
                        }
                    }
                }
            };

            var route = "/api/v1/operations";

            // Act
            var (httpResponse, responseDocument) = await _testContext.ExecutePostAtomicAsync<ErrorDocument>(route, requestBody);

            // Assert
            httpResponse.Should().HaveStatusCode(HttpStatusCode.UnprocessableEntity);

            responseDocument.Errors.Should().HaveCount(1);
            responseDocument.Errors[0].StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            responseDocument.Errors[0].Title.Should().Be("Failed to deserialize request body: Expected data[] element for to-many relationship.");
            responseDocument.Errors[0].Detail.Should().Be("Expected data[] element for 'tracks' relationship.");
            responseDocument.Errors[0].Source.Pointer.Should().Be("/atomic:operations[0]");
        }
    }
}
