// Copyright (c) Kevin Cantu <me@kevincantu.org>
//
// redux - manipulate old Tumblr posts


open System.Collections.Generic
open System.Net
open System.IO
open System.Threading
open System.Text.RegularExpressions
open System.Xml
open System.Drawing


// command line args /////////////////////////////////////////////

let args = System.Environment.GetCommandLineArgs()
if args.Length <> 4
then
   printfn "Usage: redux.exe BLOG EMAIL PASSWORD"
   exit 1

let [| _; (blog: string); (email: string); (password: string) |] = args


// misc /////////////////////////////////////////////

// utility, combines key/values into a string with = and &
let combine (m: Map<string,string>) : string = 
   Map.fold (fun state key v -> 
                  let next = key + "=" + v
                  match state with
                  | "" ->               next
                  | _  -> state + "&" + next) 
            ""
            m


// fetch a URL /////////////////////////////////////////////

// HTTP GET
let getDocRaw (url: string) (data: Map<string,string> ) : string = 
   Async.RunSynchronously(async {
      let url' = url + "?" + (combine data)

      let req        = WebRequest.Create(url', Timeout=5000)
      use! response  = req.AsyncGetResponse()
      use reader     = new StreamReader(response.GetResponseStream())
      let output = reader.ReadToEnd()
      return output
   })


// HTTP POST
let postDocRaw (url: string) (data: Map<string,string>) : string =
   Async.RunSynchronously(async {
      let data' : byte[] = System.Text.Encoding.ASCII.GetBytes(combine data);

      let request = WebRequest.Create(url, Timeout=5000)
      request.Method        <- "POST"
      request.ContentType   <- "application/x-www-form-urlencoded"
      request.ContentLength <- (int64) data'.Length

      use wstream = request.GetRequestStream() 
      wstream.Write(data',0, (data'.Length))
      wstream.Flush()
      wstream.Close()

      use! response = request.AsyncGetResponse()
      use reader    = new StreamReader(response.GetResponseStream())
      let output    = reader.ReadToEnd()

      reader.Close()
      response.Close()
      request.Abort()

      return output
   })


// simple queries /////////////////////////////////////////////

// returns XML
let readPosts ((start,num): int*int) : string = 
      let url  = "http://" + blog + ".tumblr.com/api/read"
      let data = Map.ofList [ "start", (sprintf "%d" start);
                              "num",   (sprintf "%d" num);
                              "type",  "photo" ]

      printfn "-> reading..."

      getDocRaw url data


// returns status
let deletePost (id: string) : string =
      let url  = "http://www.tumblr.com/api/delete"
      let data = Map.ofList [ "email",    email;
                              "password", password;
                              "post-id",  id ]

      printfn "-# deleting..."

      let status = postDocRaw url data

      printfn "   status: '%s'" status

      status
                     

// returns new id; often works even though Tumblr returns an error
let reblogPost (id: string) (rkey: string) : string =
      let url  = "http://www.tumblr.com/api/reblog"
      let data = Map.ofList [ "email",      email; 
                              "password",   password; 
                              "post-id",    id; 
                              "reblog-key", rkey ]

      printfn "-* reblogging id='%s' rkey='%s'..." id rkey

      let newid = postDocRaw url data

      printfn "   newid: '%s'" newid

      newid


// process XML results /////////////////////////////////////////////

// after getPosts
let processPosts (postsXML) =
   let doc = new XmlDocument()
   postsXML |> doc.LoadXml // so doc is mutable?

   // add prettier printing
   //fsi.AddPrinter( fun (x:XmlNode) -> x.OuterXml );;

   // process the data
   let tumblr = doc.ChildNodes.Item(1)
   let posts  = tumblr.ChildNodes.Item(1)

   // overall statistics
   let start = System.Convert.ToInt32(posts.Attributes.GetNamedItem("start").Value)
   let total = System.Convert.ToInt32(posts.Attributes.GetNamedItem("total").Value)
   let num   = posts.ChildNodes.Count

   // "2010-11-24 05:57:26 GMT"
   let processDate (datestring: string) : System.DateTime =
      (System.DateTime.ParseExact( (datestring.Split [| ' ' |]).[0], "yyyy-MM-dd", null )) 

   // posts.HasChildNodes
   let postsFound = 
      [
         for ii in 0..(num-1) do
            let post       = posts.ChildNodes.Item(ii)
            let id         = post.Attributes.GetNamedItem("id").Value
            let reblogkey  = post.Attributes.GetNamedItem("reblog-key").Value
            let date       = post.Attributes.GetNamedItem("date-gmt").Value |> processDate
            yield (id, reblogkey, date, post)
      ]

   // display a post tuple
   let display (id, reblogkey, date, post:XmlNode) = 
      let pic = post.ChildNodes.Item(1).InnerText
      printfn "   id: '%s', rkey: '%s', '%s'\n   %s" id reblogkey (date.ToString()) pic

   // print stats
   printfn "   read %d to %d of %d" start (start+num-1) total |> ignore

   // print all
   postsFound |> List.map display |> ignore

   (start, total, postsFound)


// compose read and process /////////////////////////////////////////////
let readAndProcessPosts = readPosts >> processPosts


// range to consider ////////////////////////////////

// get the most recent post on a given date
let rangeEndingIn (targetDate: System.DateTime) : int*int = 

   // date of post /////////////////////////////////////////////
   let dateOfPost (index: int) : System.DateTime = 
      let (start, total, posts) = readAndProcessPosts (index, 1)

      // srsly, TODO: make this post tuple a type
      let (_,_,date,_) = posts |> List.head 
      date

   // if we have a match for the right date, step the the latest post on that date
   let rec walkToNewestMatch (start: int) (target: System.DateTime) : int =
      let nextPostDate = dateOfPost (start-1)
      match (nextPostDate > target) with
      | true  -> start
      | false -> walkToNewestMatch (start-1) target
      
   // binsearch to find latest post before a given date
   let rec findCutoff (target: System.DateTime) (newest: int) (oldest: int) : int = 

      // TODO: fix case where date sought is earlier than oldest post

      let middle = (newest + oldest) / 2

      let middleDate : System.DateTime = dateOfPost middle

      match (middleDate, target) with
      | a,b when newest =  oldest && a < b -> middle
      | a,b when newest =  oldest && a = b -> walkToNewestMatch middle target
      | a,b when newest =  oldest && a > b -> middle+1
      | a,b when newest <> oldest && a < b -> findCutoff target newest (middle-1)
      | a,b when newest <> oldest && a = b -> walkToNewestMatch middle target    // combine above
      | a,b when newest <> oldest && a > b -> findCutoff target (middle+1) oldest
      | _                                  -> -1 // humbug

   // get the latest post
   let (startingPostNumber, total, posts) = readAndProcessPosts (0, 1)

   // find where the end of the date we care about is
   let oldest = total - 1  // assuming Tumblr numbers from 0
   let newest = findCutoff targetDate startingPostNumber oldest

   (oldest, newest)


// RUN /////////////////////////////////////////////

let testPostReblogging ii = 
   // read some posts
   let (start, total, posts) = readAndProcessPosts (ii, 1)

   // reblog those and delete original posts
   posts |> List.map (fun (id, rkey, datestring, post) ->
         reblogPost id rkey   |> ignore
         deletePost id        |> ignore
   )


let deleteOnOrBefore (date: System.DateTime) =
      let (oldest, newest) = rangeEndingIn date
   
      // arbitrarily do requests for 30 posts at a time
      let inc = 30

      // I could make this parallel and very fast, but 
      // let's be nice to Tumblr, we love them.
      //
      // Note: reads are positional, but the id and reblog key
      // would allow us to easily do deletions or reblogging after that
      // concurrently.
      [newest..inc..oldest] 
      |> List.map (fun jj -> 
            let (_, _, posts) = readAndProcessPosts (jj, inc)
            posts)
      |> List.concat
      |> List.map (fun (id, rkey, _, _) -> 
            Async.RunSynchronously(Async.Sleep(5*1000)) |> ignore
            reblogPost id rkey |> ignore

            Async.RunSynchronously(Async.Sleep(5*1000)) |> ignore
            deletePost id |> ignore)
   

let testDeletion() =
   deleteOnOrBefore (System.DateTime(2009,6,19))


testPostReblogging 270 |> ignore

testDeletion() |> ignore
