# TODO:
# * front-end for Tumblr session auth
# * callback page for Tumblr auth
# * page for archiving
# * page for deletions

from flask import Flask, render_template, request, session, redirect, url_for

from appconfig import read_config

import tumblr_auth


conf = read_config()
app = Flask('ninlil')
app.config.update(
    DEBUG = True,
    SECRET_KEY = conf['flask_secret_key'],
)


@app.route('/')
def index():
    return render_template('index.jinja.html')


@app.route('/tumblr')
def tumblr_start():
    return render_template('tumblr.jinja.html')


@app.route('/tumblr_save_auth', methods=['POST'])
def tumblr_save_auth():
    '''
    Given the user's blog URL, 
    start OAuth and redirect to Tumblr's auth URL.
    '''
    session['tumblr_save__blog_url'] = request.values.get('blog_url')
    session['tumblr_save__email'] = request.values.get('email')
    session['tumblr_save__start_date'] = request.values.get('start_date')
    session['tumblr_save__end_date'] = request.values.get('end_date')

    'Set up an auth URL which will redirect back to another of our routes.'
    next_route = url_for('tumblr_save_confirm', _external = True)
    auth_url, auth_token_secret = tumblr_auth.oauth_initial_auth(next_route)

    'Save the secret.'
    session['tumblr_save__oauth_token_secret'] = auth_token_secret

    'Proceed with OAuth.'
    return redirect(auth_url)


@app.route('/tumblr_save_confirm', methods=['GET'])
def tumblr_save_confirm():
    '''
    Given an OAuth token and verifier from Tumblr (and our secret from the session),
    derive the final OAuth token and verifier,
    then save photos to a file and provide a confirmation/download page.

    This may be slow, but TODO: spin off something async which emails users.
    '''

    'Finish OAuth.'
    final_oauth_token, final_oauth_token_secret = tumblr_auth.oauth_verify_user_token(
        session['tumblr_save__oauth_token_secret'],
        request.values.get('oauth_token'),
        request.values.get('oauth_verifier'),
    )
    session['tumblr_save__final_oauth_token'] = final_oauth_token
    session['tumblr_save__final_oauth_token_secret'] = final_oauth_token_secret

    'Download photos.'
    
    return render_template('tumblr_save_confirm.jinja.html')


@app.route('/tumblr_delete_auth', methods=['POST'])
def tumblr_delete_auth():
    pass


@app.route('/tumblr_delete_confirm', methods=['POST'])
def tumblr_delete_confirm():
    pass


if __name__ == '__main__':
    app.run('localhost')
