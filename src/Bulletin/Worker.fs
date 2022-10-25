module Worker

open System.Threading
open System.Threading.Tasks
open Microsoft.Extensions.Hosting
open FSharp.Data
open System
open Data
open FsToolkit.ErrorHandling
open Marten
open Marten.Linq
open Microsoft.Extensions.DependencyInjection

type RSS = XmlProvider<"http://rss.cnn.com/rss/cnn_topstories.rss">

let pollingTimespan = TimeSpan.FromMinutes(30)

let sources =
    [ "http://rss.cnn.com/rss/cnn_topstories.rss"
      "https://feeds.nbcnews.com/nbcnews/public/news"
      "https://globalnews.ca/feed" ]

let toDateTime (offset: DateTimeOffset) = offset.UtcDateTime

let toPostView (item: RSS.Item) =
    { Id = Guid.NewGuid()
      Headline = item.Title
      Published = toDateTime item.PubDate.Value
      Link = item.Link
      AuthorId = None |> Option.toNullable // TODO: Any way to use an option here instead of nullable?
      Votes = []
      Comments = []
      Score = 0 }

let filterSource (latest: DateTime option) (post: Post) =
    match latest with
    | None -> true
    | Some latest -> post.Published > latest

let readSourceAsync (latest: DateTime option) (source: string) =
    task {
        let! rssResult = RSS.AsyncLoad(source) |> Async.Catch

        return
            match rssResult with
            | Choice1Of2 rss ->
                rss.Channel.Items
                |> Array.distinctBy (fun item -> item.Guid) // idk?
                |> Array.filter (fun item -> Option.isSome item.PubDate)
                |> Array.map toPostView
                |> Array.filter (filterSource latest)
                |> Array.toList
            | Choice2Of2 ex ->
                // TODO: I need to do proper logging.
                printfn "An exception has occured: %s" ex.StackTrace
                []
    }

type IScopedBackgroundService =
    abstract member DoWorkAsync: CancellationToken -> Task

type RssWorker(querySession: IQuerySession, documentSession: IDocumentSession) =
    interface IScopedBackgroundService with
        member _.DoWorkAsync(stoppingToken: CancellationToken) =
            task {
                while not (stoppingToken.IsCancellationRequested) do
                    let! latestPost = querySession |> latestPostAsync
                    let! results = sources |> List.map (readSourceAsync latestPost) |> Task.WhenAll

                    let posts =
                        results
                        |> Array.toList
                        |> List.filter (fun posts -> not (List.isEmpty posts))
                        |> List.collect id

                    if not (List.isEmpty posts) then
                        // TODO: this can cause an issue if the same link *somehow* appears twice in an rss feed.
                        // This can happen when live updates or editing/revisions cause a new entry in the rss feed.
                        // This is my current theory although it may be wrong.
                        // The unique index on the link/url will cause an exception and cancel the batch insert.
                        // I could insert posts one at a time because there *should* only be a few updates per poll each time.
                        // (unless we're doing a cold start and reading the entire feed and saving everything)
                        // think about how to solve this while keeping batch inserts??? or maybe not, idk.
                        documentSession |> Session.storeMany posts
                        do! documentSession |> Session.saveChangesTask CancellationToken.None

                    do! Task.Delay(pollingTimespan)
            }

type RssWorkerBackgroundService(serviceProvider: IServiceProvider) =
    inherit BackgroundService()

    override _.ExecuteAsync(stoppingToken: CancellationToken) : Task =
        task {
            use scope = serviceProvider.CreateScope()
            let worker = scope.ServiceProvider.GetService<IScopedBackgroundService>()
            do! worker.DoWorkAsync(stoppingToken)
        }
