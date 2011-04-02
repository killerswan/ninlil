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

// combines key/values into a string with = and &
// for use with HTTP GET and POST
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
// Note: the point of the async {} is to try not to block so much,
// but in this program it is over-engineering. :D
let getDocRaw (url: string) (data: Map<string,string> ) : string = 
   Async.RunSynchronously(async {
      let url' = url + "?" + (combine data)

      let req        = WebRequest.Create(url', Timeout=15000)
      use! response  = req.AsyncGetResponse()
      use reader     = new StreamReader(response.GetResponseStream())
      let output = reader.ReadToEnd()
      return output
   })


// HTTP POST
let postDocRaw (url: string) (data: Map<string,string>) : string =
   Async.RunSynchronously(async {
      let data' : byte[] = System.Text.Encoding.ASCII.GetBytes(combine data);

      let request = WebRequest.Create(url, Timeout=15000)  // sensitive to too short a delay
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

// read via personal Tumblr API
let readPosts ((start,num): int*int) : string = 
      let url  = "http://" + blog + ".tumblr.com/api/read"
      let data = Map.ofList [ "start", (sprintf "%d" start);
                              "num",   (sprintf "%d" num);
                              "type",  "photo" ]

      printfn "-> reading..."

      let xml = getDocRaw url data
      xml


// delete using Tumblr API
let deletePost (id: string) : string =
      let url  = "http://www.tumblr.com/api/delete"
      let data = Map.ofList [ "email",    email;
                              "password", password;
                              "post-id",  id ]

      printfn "-# deleting..."

      let status = postDocRaw url data

      printfn "   status: '%s'" status

      status
                     

// reblog using Tumblr API
// often works even though Tumblr returns an error
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


// read and process /////////////////////////////////////////////

// simplify
let readAndProcessPosts = readPosts >> processPosts

// test
let testPostReblogging ii = 
   // read a post (or list of posts)
   let (start, total, posts) = readAndProcessPosts (ii, 1)

   // reblog and delete that post (or list of posts)
   posts |> List.map (fun (id, rkey, datestring, post) ->
         reblogPost id rkey   |> ignore
         deletePost id        |> ignore
   )


// dealing with a range ////////////////////////////////

// get the most recent post on a given date
let rangeEndingIn (targetDate: System.DateTime) : int*int = 

   // date of post /////////////////////////////////////////////
   let dateOfPost (index: int) : System.DateTime = 
      let (start, total, posts) = readAndProcessPosts (index, 1)

      // srsly, TODO: make this post tuple a type
      let (_,_,date,_) = posts |> List.head 
      date


   // if we have a match for the right date, step the the latest post on that date
   let rec walkToNewestMatch (target: System.DateTime) (start: int) : int =
      let nextPostDate = dateOfPost (start-1)

      printfn "   {%d, %d}" (start-1) start

      match (nextPostDate > target) with
      | true  -> start
      | false -> walkToNewestMatch target (start-1)
      

   // binsearch to find latest post on a given date
   let rec findCutoff (target: System.DateTime) (newest: int) (oldest: int) : int = 

      if (newest + 1) = oldest then
         if target < (dateOfPost oldest) then
            oldest + 1
         else
            if target < (dateOfPost newest) then
               oldest
            else
               newest

      else
         let middle = (newest + oldest) / 2

         if target < (dateOfPost middle) then
            findCutoff        target (middle+1) oldest
         elif target > (dateOfPost middle) then
            findCutoff        target newest (middle-1)
         else 
            walkToNewestMatch target middle


   // get the latest post
   let (startingPostNumber, total, posts) = readAndProcessPosts (0, 1)

   // find where the end of the date we care about is
   let oldest = total - 1  // assuming Tumblr numbers from 0
   let newest = findCutoff targetDate startingPostNumber oldest

   (oldest, newest)


// delete the range of posts on or before a given date
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
            Async.RunSynchronously(Async.Sleep(10*1000)) |> ignore
            let (_, _, posts) = readAndProcessPosts (jj, inc)
            posts)
      |> List.concat  // condense our array of post arrays
      |> List.map (fun (id, rkey, _, _) -> 
(* 
            Async.RunSynchronously(Async.Sleep(5*1000)) |> ignore  // is there some obvious sleep command?
            reblogPost id rkey |> ignore
*)
            Async.RunSynchronously(Async.Sleep(10*1000)) |> ignore
            deletePost id |> ignore)
   

// run /////////////////////////////////////////////

//testPostReblogging 270 |> ignore

deleteOnOrBefore (System.DateTime(2010,1,1))
|> ignore


